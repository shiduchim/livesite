// EKGlyphHook.dll - replaces the themed tree expand/collapse glyph
// (uxtheme class TREEVIEW, part TVP_GLYPH / TVP_HOTGLYPH) inside the process
// that loads it, i.e. Revit only. Nothing on disk is modified: the hooks live
// in memory and disappear when Revit exits.
//
// Why this point: DesktopMFC.dll's MFCTreeBase<SECTreeCtrl>::DrawButton and
// MFCTreeBase<SECTreeView>::DrawButton both call
//   RWOpenThemeData(hwnd, L"TREEVIEW")
//   RWDrawThemeBackground(h, hdc, TVP_GLYPH, expanded ? GLPS_OPENED : GLPS_CLOSED, &rc, NULL)
// and RWUXThemeSU2015.dll forwards those straight to uxtheme.dll through
// GetProcAddress pointers. comctl32's SysTreeView32 uses the same uxtheme
// part. Hooking uxtheme's entry points therefore covers every themed caller.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <uxtheme.h>
#include <vssym32.h>
#include <objidl.h>
#include <gdiplus.h>

#include <algorithm>
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <cwchar>
#include <memory>
#include <set>
#include <string>
#include <unordered_map>
#include <vector>

#include "MinHook.h"
#include "svgpath.h"

#ifdef _MSC_VER
#include <intrin.h>
#pragma intrinsic(_ReturnAddress)
#define RETADDR() _ReturnAddress()
#else
#define RETADDR() __builtin_return_address(0)
#endif

namespace {

// ---------------------------------------------------------------- settings --

struct Glyph {
    std::vector<svg::Figure> figures;
    std::unique_ptr<Gdiplus::Bitmap> bitmap;
    bool valid() const { return bitmap || !figures.empty(); }
};

struct Settings {
    bool enabled = true;
    bool log = false;
    bool smoothing = true;
    bool sysColor = false;
    BYTE a = 255, r = 0x2B, g = 0x57, b = 0x9A;
    double vbX = 0, vbY = 0, vbW = 16, vbH = 16;
    std::set<int> parts{TVP_GLYPH, TVP_HOTGLYPH};
    Glyph closed, opened;
};

Settings g_cfg;
std::wstring g_dir;
std::wstring g_logPath;
bool g_hooked = false;
ULONG_PTR g_gdiplusToken = 0;

// Default design: closed = "T", opened = the T's bar only. Replace both in
// glyph.ini with your own SVG path data.
const char* kDefaultClosed = "M3 3H13V5.5H9.25V13H6.75V5.5H3Z";
const char* kDefaultOpened = "M3 3H13V5.5H3Z";

std::wstring IniString(const wchar_t* key, const wchar_t* def) {
    std::wstring ini = g_dir + L"\\glyph.ini";
    std::vector<wchar_t> buf(8192);
    GetPrivateProfileStringW(L"Glyph", key, def, buf.data(), static_cast<DWORD>(buf.size()), ini.c_str());
    return buf.data();
}

std::string Narrow(const std::wstring& w) {
    std::string s;
    for (wchar_t c : w) s.push_back(c < 128 ? static_cast<char>(c) : ' ');
    return s;
}

std::wstring Trim(std::wstring s) {
    const wchar_t* ws = L" \t\r\n\"";
    size_t a = s.find_first_not_of(ws), b = s.find_last_not_of(ws);
    return a == std::wstring::npos ? L"" : s.substr(a, b - a + 1);
}

// ------------------------------------------------------------------- log ----

void Log(const char* fmt, ...) {
    if (!g_cfg.log || g_logPath.empty()) return;
    char line[1024];
    SYSTEMTIME st;
    GetLocalTime(&st);
    int n = _snprintf_s(line, sizeof(line), _TRUNCATE, "%04u-%02u-%02u %02u:%02u:%02u.%03u [%lu] ",
                        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
                        GetCurrentThreadId());
    va_list ap;
    va_start(ap, fmt);
    int m = _vsnprintf_s(line + n, sizeof(line) - n - 2, _TRUNCATE, fmt, ap);
    va_end(ap);
    int len = n + (m < 0 ? static_cast<int>(strlen(line + n)) : m);
    line[len++] = '\r';
    line[len++] = '\n';
    HANDLE h = CreateFileW(g_logPath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                           OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    DWORD w;
    WriteFile(h, line, static_cast<DWORD>(len), &w, nullptr);
    CloseHandle(h);
}

std::string Where(void* addr) {
    HMODULE mod = nullptr;
    char name[MAX_PATH] = "?";
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           static_cast<LPCSTR>(addr), &mod)) {
        char path[MAX_PATH];
        if (GetModuleFileNameA(mod, path, MAX_PATH)) {
            const char* slash = strrchr(path, '\\');
            strcpy_s(name, slash ? slash + 1 : path);
        }
    }
    char out[MAX_PATH + 32];
    _snprintf_s(out, sizeof(out), _TRUNCATE, "%s+0x%llX", name,
                static_cast<unsigned long long>(reinterpret_cast<BYTE*>(addr) - reinterpret_cast<BYTE*>(mod)));
    return out;
}

// One log line per distinct (caller, part, state), so the log stays small.
SRWLOCK g_logLock = SRWLOCK_INIT;
std::set<std::string> g_seen;

void LogOnce(const std::string& key, const char* fmt, ...) {
    if (!g_cfg.log) return;
    AcquireSRWLockExclusive(&g_logLock);
    bool fresh = g_seen.size() < 500 && g_seen.insert(key).second;
    ReleaseSRWLockExclusive(&g_logLock);
    if (!fresh) return;
    char msg[700];
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf_s(msg, sizeof(msg), _TRUNCATE, fmt, ap);
    va_end(ap);
    Log("%s", msg);
}

// ------------------------------------------------------- theme tracking ----

// HTHEME -> open count, only for handles whose class list names TreeView.
SRWLOCK g_themeLock = SRWLOCK_INIT;
std::unordered_map<HTHEME, int> g_treeThemes;
thread_local int t_depth = 0;  // uxtheme may call its own exports internally

bool NamesTreeView(LPCWSTR list) {
    if (!list) return false;
    std::wstring s(list);
    size_t pos = 0;
    while (pos <= s.size()) {
        size_t end = s.find(L';', pos);
        if (end == std::wstring::npos) end = s.size();
        std::wstring tok = s.substr(pos, end - pos);
        size_t colons = tok.rfind(L"::");
        if (colons != std::wstring::npos) tok = tok.substr(colons + 2);
        if (_wcsicmp(Trim(tok).c_str(), L"TreeView") == 0) return true;
        pos = end + 1;
    }
    return false;
}

void TrackOpen(HTHEME h, LPCWSTR list, void* caller) {
    if (!h || !NamesTreeView(list)) return;
    AcquireSRWLockExclusive(&g_themeLock);
    ++g_treeThemes[h];
    ReleaseSRWLockExclusive(&g_themeLock);
    std::string where = Where(caller);
    LogOnce("open|" + where, "OpenThemeData(\"%ls\") from %s", list, where.c_str());
}

bool IsTreeTheme(HTHEME h) {
    AcquireSRWLockShared(&g_themeLock);
    bool yes = g_treeThemes.count(h) != 0;
    ReleaseSRWLockShared(&g_themeLock);
    return yes;
}

// ----------------------------------------------------------------- hooks ----

typedef HTHEME(WINAPI* OpenThemeData_t)(HWND, LPCWSTR);
typedef HTHEME(WINAPI* OpenThemeDataEx_t)(HWND, LPCWSTR, DWORD);
typedef HTHEME(WINAPI* OpenThemeDataForDpi_t)(HWND, LPCWSTR, UINT);
typedef HRESULT(WINAPI* CloseThemeData_t)(HTHEME);
typedef HRESULT(WINAPI* DrawThemeBackground_t)(HTHEME, HDC, int, int, LPCRECT, LPCRECT);
typedef HRESULT(WINAPI* DrawThemeBackgroundEx_t)(HTHEME, HDC, int, int, LPCRECT, const DTBGOPTS*);

OpenThemeData_t o_OpenThemeData;
OpenThemeDataEx_t o_OpenThemeDataEx;
OpenThemeDataForDpi_t o_OpenThemeDataForDpi;
CloseThemeData_t o_CloseThemeData;
DrawThemeBackground_t o_DrawThemeBackground;
DrawThemeBackgroundEx_t o_DrawThemeBackgroundEx;

struct Depth {
    Depth() { ++t_depth; }
    ~Depth() { --t_depth; }
    bool outermost() const { return t_depth == 1; }
};

HTHEME WINAPI H_OpenThemeData(HWND w, LPCWSTR cls) {
    Depth d;
    HTHEME h = o_OpenThemeData(w, cls);
    if (d.outermost()) TrackOpen(h, cls, RETADDR());
    return h;
}

HTHEME WINAPI H_OpenThemeDataEx(HWND w, LPCWSTR cls, DWORD flags) {
    Depth d;
    HTHEME h = o_OpenThemeDataEx(w, cls, flags);
    if (d.outermost()) TrackOpen(h, cls, RETADDR());
    return h;
}

HTHEME WINAPI H_OpenThemeDataForDpi(HWND w, LPCWSTR cls, UINT dpi) {
    Depth d;
    HTHEME h = o_OpenThemeDataForDpi(w, cls, dpi);
    if (d.outermost()) TrackOpen(h, cls, RETADDR());
    return h;
}

HRESULT WINAPI H_CloseThemeData(HTHEME h) {
    Depth d;
    if (d.outermost()) {
        AcquireSRWLockExclusive(&g_themeLock);
        auto it = g_treeThemes.find(h);
        if (it != g_treeThemes.end() && --it->second <= 0) g_treeThemes.erase(it);
        ReleaseSRWLockExclusive(&g_themeLock);
    }
    return o_CloseThemeData(h);
}

COLORREF GlyphColor() {
    if (g_cfg.sysColor) return GetSysColor(COLOR_WINDOWTEXT);
    return RGB(g_cfg.r, g_cfg.g, g_cfg.b);
}

// Draws the custom glyph centred in rc. Returns false to fall back to Windows.
bool DrawGlyph(HDC hdc, int state, LPCRECT rc, LPCRECT clip) {
    const Glyph& gl = (state == GLPS_OPENED) ? g_cfg.opened : g_cfg.closed;
    if (!gl.valid() || !rc) return false;
    double w = rc->right - rc->left, h = rc->bottom - rc->top;
    if (w <= 0 || h <= 0) return true;

    Gdiplus::Graphics gfx(hdc);
    if (gfx.GetLastStatus() != Gdiplus::Ok) return false;
    if (clip)
        gfx.SetClip(Gdiplus::Rect(clip->left, clip->top, clip->right - clip->left, clip->bottom - clip->top),
                    Gdiplus::CombineModeIntersect);

    if (gl.bitmap) {
        gfx.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
        gfx.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);
        double bw = gl.bitmap->GetWidth(), bh = gl.bitmap->GetHeight();
        double s = std::min(w / bw, h / bh);
        Gdiplus::RectF dst(static_cast<Gdiplus::REAL>(rc->left + (w - bw * s) / 2),
                           static_cast<Gdiplus::REAL>(rc->top + (h - bh * s) / 2),
                           static_cast<Gdiplus::REAL>(bw * s), static_cast<Gdiplus::REAL>(bh * s));
        return gfx.DrawImage(gl.bitmap.get(), dst) == Gdiplus::Ok;
    }

    gfx.SetSmoothingMode(g_cfg.smoothing ? Gdiplus::SmoothingModeAntiAlias : Gdiplus::SmoothingModeNone);
    gfx.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);
    double s = std::min(w / g_cfg.vbW, h / g_cfg.vbH);
    double ox = rc->left + (w - g_cfg.vbW * s) / 2 - g_cfg.vbX * s;
    double oy = rc->top + (h - g_cfg.vbH * s) / 2 - g_cfg.vbY * s;
    auto P = [&](const svg::Pt& p) {
        return Gdiplus::PointF(static_cast<Gdiplus::REAL>(ox + p.x * s), static_cast<Gdiplus::REAL>(oy + p.y * s));
    };
    Gdiplus::GraphicsPath path(Gdiplus::FillModeWinding);
    for (const auto& f : gl.figures) {
        path.StartFigure();
        for (size_t i = 1; i + 2 < f.pts.size(); i += 3)
            path.AddBezier(P(f.pts[i - 1]), P(f.pts[i]), P(f.pts[i + 1]), P(f.pts[i + 2]));
        if (f.closed) path.CloseFigure();
    }
    COLORREF c = GlyphColor();
    Gdiplus::SolidBrush brush(Gdiplus::Color(g_cfg.a, GetRValue(c), GetGValue(c), GetBValue(c)));
    return gfx.FillPath(&brush, &path) == Gdiplus::Ok;
}

bool TryReplace(HTHEME h, HDC hdc, int part, int state, LPCRECT rc, LPCRECT clip, void* caller) {
    if (!g_cfg.parts.count(part) || !IsTreeTheme(h)) return false;
    bool ok = DrawGlyph(hdc, state, rc, clip);
    std::string where = Where(caller);
    char key[MAX_PATH + 64];
    _snprintf_s(key, sizeof(key), _TRUNCATE, "draw|%s|%d|%d", where.c_str(), part, state);
    LogOnce(key, "TREEVIEW part=%d state=%d rect=%ldx%ld from %s -> %s", part, state,
            rc ? rc->right - rc->left : 0, rc ? rc->bottom - rc->top : 0, where.c_str(),
            ok ? "REPLACED" : "fallback to Windows");
    return ok;
}

HRESULT WINAPI H_DrawThemeBackground(HTHEME h, HDC hdc, int part, int state, LPCRECT rc, LPCRECT clip) {
    Depth d;
    if (d.outermost() && TryReplace(h, hdc, part, state, rc, clip, RETADDR())) return S_OK;
    return o_DrawThemeBackground(h, hdc, part, state, rc, clip);
}

HRESULT WINAPI H_DrawThemeBackgroundEx(HTHEME h, HDC hdc, int part, int state, LPCRECT rc, const DTBGOPTS* o) {
    Depth d;
    bool plain = !o || !(o->dwFlags & (DTBG_COMPUTINGREGION | DTBG_OMITCONTENT));
    if (d.outermost() && plain) {
        LPCRECT clip = (o && (o->dwFlags & DTBG_CLIPRECT)) ? &o->rcClip : nullptr;
        if (TryReplace(h, hdc, part, state, rc, clip, RETADDR())) return S_OK;
    }
    return o_DrawThemeBackgroundEx(h, hdc, part, state, rc, o);
}

// ------------------------------------------------------------- loading -----

bool LoadGlyph(Glyph& gl, const wchar_t* pathKey, const wchar_t* pngKey, const char* def) {
    std::wstring png = Trim(IniString(pngKey, L""));
    if (!png.empty()) {
        if (png.find(L':') == std::wstring::npos) png = g_dir + L"\\" + png;
        gl.bitmap.reset(Gdiplus::Bitmap::FromFile(png.c_str()));
        if (gl.bitmap && gl.bitmap->GetLastStatus() == Gdiplus::Ok && gl.bitmap->GetWidth() > 0) {
            Log("%ls: PNG %ls (%ux%u)", pngKey, png.c_str(), gl.bitmap->GetWidth(), gl.bitmap->GetHeight());
            return true;
        }
        Log("%ls: could not load PNG %ls", pngKey, png.c_str());
        gl.bitmap.reset();
    }
    std::wstring d = Trim(IniString(pathKey, L""));
    std::string data = d.empty() ? std::string(def) : Narrow(d);
    if (!svg::Parse(data, gl.figures)) {
        Log("%ls: invalid SVG path data, using default", pathKey);
        svg::Parse(def, gl.figures);
    }
    return gl.valid();
}

void LoadSettings() {
    g_cfg.enabled = Trim(IniString(L"Enabled", L"1")) != L"0";
    g_cfg.log = Trim(IniString(L"Log", L"0")) == L"1";
    g_cfg.smoothing = Trim(IniString(L"Smoothing", L"1")) != L"0";

    std::wstring color = Trim(IniString(L"Color", L"#2B579A"));
    g_cfg.sysColor = _wcsicmp(color.c_str(), L"WindowText") == 0;
    if (!g_cfg.sysColor && !color.empty() && color[0] == L'#') {
        unsigned long v = wcstoul(color.c_str() + 1, nullptr, 16);
        bool hasAlpha = color.size() == 9;
        g_cfg.a = hasAlpha ? static_cast<BYTE>(v >> 24) : 255;
        g_cfg.r = static_cast<BYTE>(v >> 16);
        g_cfg.g = static_cast<BYTE>(v >> 8);
        g_cfg.b = static_cast<BYTE>(v);
    }

    double vb[4];
    std::wstring vbs = IniString(L"ViewBox", L"0 0 16 16");
    if (swscanf_s(vbs.c_str(), L"%lf %lf %lf %lf", &vb[0], &vb[1], &vb[2], &vb[3]) == 4 && vb[2] > 0 && vb[3] > 0) {
        g_cfg.vbX = vb[0];
        g_cfg.vbY = vb[1];
        g_cfg.vbW = vb[2];
        g_cfg.vbH = vb[3];
    }

    std::wstring parts = IniString(L"Parts", L"2,4");
    g_cfg.parts.clear();
    for (wchar_t* p = &parts[0]; *p;) {
        wchar_t* end;
        long v = wcstol(p, &end, 10);
        if (end == p) { ++p; continue; }
        g_cfg.parts.insert(static_cast<int>(v));
        p = end;
    }
}

template <class T>
bool Hook(const char* name, LPVOID detour, T* original, bool required) {
    MH_STATUS st = MH_CreateHookApi(L"uxtheme.dll", name, detour, reinterpret_cast<LPVOID*>(original));
    if (st != MH_OK) Log("hook %s: %s%s", name, MH_StatusToString(st), required ? "" : " (optional)");
    return st == MH_OK || !required;
}

}  // namespace

extern "C" __declspec(dllexport) int EKGlyph_Init(const wchar_t* dir) {
    if (g_hooked) return 0;
    g_dir = dir ? dir : L".";
    LoadSettings();

    wchar_t local[MAX_PATH];
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", local, MAX_PATH)) {
        std::wstring logDir = std::wstring(local) + L"\\EKGlyph";
        CreateDirectoryW(logDir.c_str(), nullptr);
        g_logPath = logDir + L"\\EKGlyph.log";
    }
    Log("---- EKGlyph init, pid %lu, config %ls\\glyph.ini", GetCurrentProcessId(), g_dir.c_str());
    if (!g_cfg.enabled) {
        Log("Enabled=0: nothing hooked");
        return 1;
    }

    Gdiplus::GdiplusStartupInput in;
    if (Gdiplus::GdiplusStartup(&g_gdiplusToken, &in, nullptr) != Gdiplus::Ok) {
        Log("GdiplusStartup failed");
        return -1;
    }
    LoadGlyph(g_cfg.closed, L"Closed", L"ClosedPng", kDefaultClosed);
    LoadGlyph(g_cfg.opened, L"Opened", L"OpenedPng", kDefaultOpened);

    LoadLibraryW(L"uxtheme.dll");
    if (MH_Initialize() != MH_OK) {
        Log("MH_Initialize failed");
        return -2;
    }
    bool ok = Hook("OpenThemeData", reinterpret_cast<LPVOID>(&H_OpenThemeData), &o_OpenThemeData, true) &&
              Hook("CloseThemeData", reinterpret_cast<LPVOID>(&H_CloseThemeData), &o_CloseThemeData, true) &&
              Hook("DrawThemeBackground", reinterpret_cast<LPVOID>(&H_DrawThemeBackground), &o_DrawThemeBackground, true);
    Hook("OpenThemeDataEx", reinterpret_cast<LPVOID>(&H_OpenThemeDataEx), &o_OpenThemeDataEx, false);
    Hook("OpenThemeDataForDpi", reinterpret_cast<LPVOID>(&H_OpenThemeDataForDpi), &o_OpenThemeDataForDpi, false);
    Hook("DrawThemeBackgroundEx", reinterpret_cast<LPVOID>(&H_DrawThemeBackgroundEx), &o_DrawThemeBackgroundEx, false);
    if (!ok || MH_EnableHook(MH_ALL_HOOKS) != MH_OK) {
        Log("enabling hooks failed; leaving Revit untouched");
        MH_Uninitialize();
        return -3;
    }
    g_hooked = true;
    Log("hooks active (parts:%s%s)", g_cfg.parts.count(TVP_GLYPH) ? " TVP_GLYPH" : "",
        g_cfg.parts.count(TVP_HOTGLYPH) ? " TVP_HOTGLYPH" : "");
    return 0;
}

// Called from the add-in's OnShutdown. Hooks are only disabled (not removed,
// and the DLL is never unloaded) so a paint in flight can never land in freed code.
extern "C" __declspec(dllexport) void EKGlyph_Shutdown() {
    if (!g_hooked) return;
    MH_DisableHook(MH_ALL_HOOKS);
    g_hooked = false;
    Log("hooks disabled");
}
