// EKTriangleHook.dll - replaces the themed tree expand/collapse glyph
// (uxtheme class TREEVIEW, part TVP_GLYPH / TVP_HOTGLYPH) inside Revit.exe.
// Nothing on disk is modified: the hooks live in memory and disappear when
// Revit exits.
//
// Baseline: EKGlyphHook.cpp from the EKTreeGlyphProbe2024 test that was
// verified in Revit 2024 (glyphs replaced, expand/collapse unchanged). The
// interception points and theme tracking are unchanged. Changes for the
// release build:
//   * refuses to hook unless the host process is Revit.exe
//   * refuses to hook if the legacy EKGlyphHook.dll or a second copy of this
//     DLL is already active in the process (no duplicate hooks)
//   * default glyph is a pixel-snapped solid triangle (#555555) sized from
//     the rectangle Revit asks for, so it stays sharp at every DPI
//   * only the two known glyph states are replaced; anything else is drawn
//     by Windows as before
//   * a fault while drawing turns replacement off for the rest of the
//     session instead of propagating into Revit (MSVC builds)
//   * log file is bounded in size
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
#include <atomic>
#include <cmath>
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

#define EKT_STR2(x) #x
#define EKT_STR(x) EKT_STR2(x)
#ifndef EKT_VERSION
#define EKT_VERSION 1.0.0
#endif

namespace {

// Status codes returned by EKTriangle_Init (also written to the log).
enum InitStatus {
    kActive = 0,
    kDisabled = 1,         // Enabled=0 in glyph.ini
    kNotRevit = 2,         // host process is not Revit.exe
    kDuplicate = 3,        // legacy EKGlyphHook.dll or another EK Triangle hook already active
    kGdiplusFailed = -1,
    kMinHookFailed = -2,
    kHookFailed = -3,
};

// ---------------------------------------------------------------- settings --

struct Glyph {
    std::vector<svg::Figure> figures;
    std::unique_ptr<Gdiplus::Bitmap> bitmap;
    bool valid() const { return bitmap || !figures.empty(); }
};

struct Settings {
    bool enabled = true;
    bool log = true;
    bool smoothing = true;
    bool sysColor = false;
    bool triangle = true;  // Style=Triangle (default) or Style=Path
    BYTE a = 255, r = 0x55, g = 0x55, b = 0x55;
    double vbX = 0, vbY = 0, vbW = 16, vbH = 16;
    std::set<int> parts{TVP_GLYPH, TVP_HOTGLYPH};
    Glyph closed, opened;
};

Settings g_cfg;
std::wstring g_dir;
std::wstring g_logPath;
bool g_hooked = false;
std::atomic<bool> g_active{false};   // detours draw only while true
std::atomic<bool> g_faulted{false};  // set after a drawing fault; replacement stays off
HANDLE g_instanceMutex = nullptr;
ULONG_PTR g_gdiplusToken = 0;
bool g_gdiplusStarted = false;

// Path-style fallback design (Style=Path with empty Closed/Opened).
const char* kDefaultClosed = "M6 4L10 8L6 12Z";
const char* kDefaultOpened = "M4 6L12 6L8 10Z";

const ULONGLONG kMaxLogBytes = 512 * 1024;

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
    if (n < 0) n = 0;
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
    LARGE_INTEGER size;
    // Hard cap: never grow the file past twice the rotation size within a session.
    if (GetFileSizeEx(h, &size) && static_cast<ULONGLONG>(size.QuadPart) < 2 * kMaxLogBytes) {
        DWORD w;
        WriteFile(h, line, static_cast<DWORD>(len), &w, nullptr);
    }
    CloseHandle(h);
}

// Keeps at most one previous log: Native.log -> Native.old.log when too big.
void RotateLog() {
    WIN32_FILE_ATTRIBUTE_DATA fa;
    if (!GetFileAttributesExW(g_logPath.c_str(), GetFileExInfoStandard, &fa)) return;
    ULONGLONG size = (static_cast<ULONGLONG>(fa.nFileSizeHigh) << 32) | fa.nFileSizeLow;
    if (size < kMaxLogBytes) return;
    std::wstring old = g_logPath.substr(0, g_logPath.size() - 4) + L".old.log";
    MoveFileExW(g_logPath.c_str(), old.c_str(), MOVEFILE_REPLACE_EXISTING);
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
    bool fresh = g_seen.size() < 200 && g_seen.insert(key).second;
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
    if (g_cfg.log) {
        std::string where = Where(caller);
        LogOnce("open|" + where, "OpenThemeData(\"%ls\") from %s", list, where.c_str());
    }
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

// Pixel-snapped solid triangle. With PixelOffsetModeHalf integer coordinates
// are pixel edges, so the straight edge lies exactly on a pixel boundary and
// the two 45-degree edges are anti-aliased symmetrically - the same result a
// browser produces for the Project Browser's 16 px SVG (M6 4L10 8L6 12Z).
// The long side is 8/9 of the glyph box (8 px at 100 % DPI), rounded to an
// even number of pixels so the apex lands on a pixel boundary too.
void AddTriangle(Gdiplus::GraphicsPath& path, bool opened, const RECT& rc) {
    int w = rc.right - rc.left, h = rc.bottom - rc.top;
    int box = std::min(w, h);
    int L = 2 * static_cast<int>(std::lround(box * 4.0 / 9.0));
    if (L < 4) L = 4;
    if (L > box && box >= 4) L = box - (box % 2);
    int half = L / 2;
    Gdiplus::Point pts[3];
    if (opened) {  // pointing down: horizontal top edge
        int x0 = rc.left + (w - L) / 2;
        int y0 = rc.top + (h - half) / 2;
        pts[0] = Gdiplus::Point(x0, y0);
        pts[1] = Gdiplus::Point(x0 + L, y0);
        pts[2] = Gdiplus::Point(x0 + half, y0 + half);
    } else {       // pointing right: vertical left edge
        int x0 = rc.left + (w - half) / 2;
        int y0 = rc.top + (h - L) / 2;
        pts[0] = Gdiplus::Point(x0, y0);
        pts[1] = Gdiplus::Point(x0 + half, y0 + half);
        pts[2] = Gdiplus::Point(x0, y0 + L);
    }
    path.AddPolygon(pts, 3);
}

// Draws the custom glyph centred in rc. Returns false to fall back to Windows.
bool DrawGlyph(HDC hdc, int state, LPCRECT rc, LPCRECT clip) {
    if (!rc) return false;
    bool opened = state == GLPS_OPENED;
    const Glyph& gl = opened ? g_cfg.opened : g_cfg.closed;
    if (!g_cfg.triangle && !gl.valid()) return false;
    double w = rc->right - rc->left, h = rc->bottom - rc->top;
    if (w <= 0 || h <= 0) return true;

    Gdiplus::Graphics gfx(hdc);
    if (gfx.GetLastStatus() != Gdiplus::Ok) return false;
    if (clip)
        gfx.SetClip(Gdiplus::Rect(clip->left, clip->top, clip->right - clip->left, clip->bottom - clip->top),
                    Gdiplus::CombineModeIntersect);

    if (!g_cfg.triangle && gl.bitmap) {
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
    Gdiplus::GraphicsPath path(Gdiplus::FillModeWinding);
    if (g_cfg.triangle) {
        AddTriangle(path, opened, *rc);
    } else {
        double s = std::min(w / g_cfg.vbW, h / g_cfg.vbH);
        double ox = rc->left + (w - g_cfg.vbW * s) / 2 - g_cfg.vbX * s;
        double oy = rc->top + (h - g_cfg.vbH * s) / 2 - g_cfg.vbY * s;
        auto P = [&](const svg::Pt& p) {
            return Gdiplus::PointF(static_cast<Gdiplus::REAL>(ox + p.x * s), static_cast<Gdiplus::REAL>(oy + p.y * s));
        };
        for (const auto& f : gl.figures) {
            path.StartFigure();
            for (size_t i = 1; i + 2 < f.pts.size(); i += 3)
                path.AddBezier(P(f.pts[i - 1]), P(f.pts[i]), P(f.pts[i + 1]), P(f.pts[i + 2]));
            if (f.closed) path.CloseFigure();
        }
    }
    COLORREF c = GlyphColor();
    Gdiplus::SolidBrush brush(Gdiplus::Color(g_cfg.a, GetRValue(c), GetGValue(c), GetBValue(c)));
    return gfx.FillPath(&brush, &path) == Gdiplus::Ok;
}

#ifdef _MSC_VER
// No C++ objects in this frame, so structured exception handling is allowed.
bool DrawGlyphGuarded(HDC hdc, int state, LPCRECT rc, LPCRECT clip, bool* faulted) {
    __try {
        return DrawGlyph(hdc, state, rc, clip);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *faulted = true;
        return false;
    }
}
#else
bool DrawGlyphGuarded(HDC hdc, int state, LPCRECT rc, LPCRECT clip, bool* faulted) {
    *faulted = false;
    try {
        return DrawGlyph(hdc, state, rc, clip);
    } catch (...) {
        *faulted = true;
        return false;
    }
}
#endif

bool TryReplace(HTHEME h, HDC hdc, int part, int state, LPCRECT rc, LPCRECT clip, void* caller) {
    if (!g_active.load(std::memory_order_acquire) || g_faulted.load(std::memory_order_relaxed)) return false;
    // TVP_GLYPH: GLPS_CLOSED=1, GLPS_OPENED=2. TVP_HOTGLYPH: HGLPS_CLOSED=1, HGLPS_OPENED=2.
    if (state != GLPS_CLOSED && state != GLPS_OPENED) return false;
    if (!g_cfg.parts.count(part) || !IsTreeTheme(h)) return false;
    bool faulted = false;
    bool ok = DrawGlyphGuarded(hdc, state, rc, clip, &faulted);
    if (faulted) {
        g_faulted = true;
        Log("drawing fault caught; custom glyphs switched off for this Revit session (Windows glyphs used)");
        return false;
    }
    if (g_cfg.log) {
        std::string where = Where(caller);
        char key[MAX_PATH + 64];
        _snprintf_s(key, sizeof(key), _TRUNCATE, "draw|%s|%d|%d|%ld", where.c_str(), part, state,
                    rc ? rc->bottom - rc->top : 0);
        LogOnce(key, "TREEVIEW part=%d state=%d rect=%ldx%ld from %s -> %s", part, state,
                rc ? rc->right - rc->left : 0, rc ? rc->bottom - rc->top : 0, where.c_str(),
                ok ? "REPLACED" : "fallback to Windows");
    }
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
    g_cfg.log = Trim(IniString(L"Log", L"1")) != L"0";
    g_cfg.smoothing = Trim(IniString(L"Smoothing", L"1")) != L"0";
    g_cfg.triangle = _wcsicmp(Trim(IniString(L"Style", L"Triangle")).c_str(), L"Path") != 0;

    std::wstring color = Trim(IniString(L"Color", L"#555555"));
    g_cfg.sysColor = _wcsicmp(color.c_str(), L"WindowText") == 0;
    if (!g_cfg.sysColor && (color.size() == 7 || color.size() == 9) && color[0] == L'#') {
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
        if (v == TVP_GLYPH || v == TVP_HOTGLYPH) g_cfg.parts.insert(static_cast<int>(v));
        p = end;
    }
}

template <class T>
bool Hook(const char* name, LPVOID detour, T* original, bool required) {
    MH_STATUS st = MH_CreateHookApi(L"uxtheme.dll", name, detour, reinterpret_cast<LPVOID*>(original));
    if (st != MH_OK) Log("hook %s: %s%s", name, MH_StatusToString(st), required ? "" : " (optional)");
    return st == MH_OK || !required;
}

bool HostIsRevit(std::wstring* exe) {
    wchar_t path[MAX_PATH * 2];
    DWORD n = GetModuleFileNameW(nullptr, path, static_cast<DWORD>(sizeof(path) / sizeof(path[0])));
    if (n == 0) return false;
    *exe = path;
    const wchar_t* slash = wcsrchr(path, L'\\');
    const wchar_t* base = slash ? slash + 1 : path;
    return _wcsicmp(base, L"Revit.exe") == 0;
}

void OpenLog() {
    wchar_t local[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", local, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return;
    std::wstring dir = std::wstring(local) + L"\\EKTriangle";
    CreateDirectoryW(dir.c_str(), nullptr);
    dir += L"\\Logs";
    CreateDirectoryW(dir.c_str(), nullptr);
    g_logPath = dir + L"\\Native.log";
    RotateLog();
}

}  // namespace

// Starts the hooks. Returns one of InitStatus. Never throws.
extern "C" __declspec(dllexport) int EKTriangle_Init(const wchar_t* configDir) {
    if (g_hooked) return kActive;
    g_dir = configDir ? configDir : L".";
    LoadSettings();
    OpenLog();
    std::wstring exe;
    bool revit = HostIsRevit(&exe);
    Log("---- EK Triangle %s native init, pid %lu, host %ls, config %ls\\glyph.ini", EKT_STR(EKT_VERSION),
        GetCurrentProcessId(), exe.c_str(), g_dir.c_str());
    if (!revit) {
        Log("host is not Revit.exe: nothing hooked");
        return kNotRevit;
    }
    if (!g_cfg.enabled) {
        Log("Enabled=0: nothing hooked");
        return kDisabled;
    }
    if (GetModuleHandleW(L"EKGlyphHook.dll")) {
        Log("legacy EKGlyphHook.dll is loaded in this process: EK Triangle stays inactive to avoid two hooks");
        return kDuplicate;
    }
    wchar_t name[96];
    _snwprintf_s(name, _TRUNCATE, L"Local\\EKTriangle.NativeHook.%lu", GetCurrentProcessId());
    g_instanceMutex = CreateMutexW(nullptr, FALSE, name);
    if (!g_instanceMutex || GetLastError() == ERROR_ALREADY_EXISTS) {
        Log("another EK Triangle hook is already active in this process: this copy stays inactive");
        if (g_instanceMutex) CloseHandle(g_instanceMutex);
        g_instanceMutex = nullptr;
        return kDuplicate;
    }

    Gdiplus::GdiplusStartupInput in;
    if (Gdiplus::GdiplusStartup(&g_gdiplusToken, &in, nullptr) != Gdiplus::Ok) {
        Log("GdiplusStartup failed");
        return kGdiplusFailed;
    }
    g_gdiplusStarted = true;
    if (!g_cfg.triangle) {
        LoadGlyph(g_cfg.closed, L"Closed", L"ClosedPng", kDefaultClosed);
        LoadGlyph(g_cfg.opened, L"Opened", L"OpenedPng", kDefaultOpened);
    }

    LoadLibraryW(L"uxtheme.dll");
    if (MH_Initialize() != MH_OK) {
        Log("MH_Initialize failed");
        return kMinHookFailed;
    }
    bool ok = Hook("OpenThemeData", reinterpret_cast<LPVOID>(&H_OpenThemeData), &o_OpenThemeData, true) &&
              Hook("CloseThemeData", reinterpret_cast<LPVOID>(&H_CloseThemeData), &o_CloseThemeData, true) &&
              Hook("DrawThemeBackground", reinterpret_cast<LPVOID>(&H_DrawThemeBackground), &o_DrawThemeBackground, true);
    if (ok) {
        Hook("OpenThemeDataEx", reinterpret_cast<LPVOID>(&H_OpenThemeDataEx), &o_OpenThemeDataEx, false);
        Hook("OpenThemeDataForDpi", reinterpret_cast<LPVOID>(&H_OpenThemeDataForDpi), &o_OpenThemeDataForDpi, false);
        Hook("DrawThemeBackgroundEx", reinterpret_cast<LPVOID>(&H_DrawThemeBackgroundEx), &o_DrawThemeBackgroundEx, false);
    }
    g_active = true;
    if (!ok || MH_EnableHook(MH_ALL_HOOKS) != MH_OK) {
        g_active = false;
        Log("enabling hooks failed; leaving Revit untouched");
        MH_DisableHook(MH_ALL_HOOKS);
        MH_Uninitialize();
        return kHookFailed;
    }
    g_hooked = true;
    Log("hooks active (style %s, colour #%02X%02X%02X, parts:%s%s)", g_cfg.triangle ? "Triangle" : "Path",
        g_cfg.r, g_cfg.g, g_cfg.b, g_cfg.parts.count(TVP_GLYPH) ? " TVP_GLYPH" : "",
        g_cfg.parts.count(TVP_HOTGLYPH) ? " TVP_HOTGLYPH" : "");
    return kActive;
}

// Called from the add-in's OnShutdown. Hooks are only disabled (not removed,
// and the DLL is never unloaded) so a paint in flight can never land in freed
// code. GDI+ is deliberately left running for the same reason; the process is
// about to exit.
extern "C" __declspec(dllexport) void EKTriangle_Shutdown() {
    g_active = false;
    if (!g_hooked) return;
    MH_DisableHook(MH_ALL_HOOKS);
    g_hooked = false;
    Log("hooks disabled (Revit shutting down)");
}

// Self-test entry used by the automated tests: draws the glyph for a state
// into hdc exactly as the hook would. Does not require hooks to be active.
extern "C" __declspec(dllexport) int EKTriangle_RenderForTest(HDC hdc, int state, const RECT* rc) {
    if (!g_gdiplusStarted) {
        Gdiplus::GdiplusStartupInput in;
        if (Gdiplus::GdiplusStartup(&g_gdiplusToken, &in, nullptr) != Gdiplus::Ok) return -1;
        g_gdiplusStarted = true;
    }
    bool faulted = false;
    return DrawGlyphGuarded(hdc, state, rc, nullptr, &faulted) ? 0 : (faulted ? -2 : 1);
}
