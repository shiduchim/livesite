// Native test harness for EKTriangleHook.dll. Built twice: as Revit.exe (the
// only host the hook accepts) and as NotRevit.exe. Each mode returns 0 on
// success, 77 when the environment cannot run the check (no visual styles),
// any other value on failure. Run by tests/native/run-native-tests.ps1 (CI)
// or under Wine by build/test-native-wine.sh.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <uxtheme.h>
#include <vssym32.h>
#include <algorithm>
#include <cstdio>
#include <initializer_list>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

typedef int(__cdecl* InitFn)(const wchar_t*);
typedef void(__cdecl* ShutdownFn)();
typedef int(__cdecl* RenderFn)(HDC, int, const RECT*);

static HMODULE g_dll;
static InitFn g_init;
static ShutdownFn g_shutdown;
static RenderFn g_render;

static int Fail(const char* msg) {
    printf("FAIL: %s\n", msg);
    return 1;
}

static bool Load(const wchar_t* path) {
    g_dll = LoadLibraryW(path);
    if (!g_dll) { printf("LoadLibrary failed (%lu)\n", GetLastError()); return false; }
    g_init = (InitFn)(void*)GetProcAddress(g_dll, "EKTriangle_Init");
    g_shutdown = (ShutdownFn)(void*)GetProcAddress(g_dll, "EKTriangle_Shutdown");
    g_render = (RenderFn)(void*)GetProcAddress(g_dll, "EKTriangle_RenderForTest");
    return g_init && g_shutdown && g_render;
}

struct Canvas {
    int w, h;
    HDC dc;
    HBITMAP bmp, old;
    unsigned* px;
    Canvas(int w_, int h_) : w(w_), h(h_) {
        BITMAPINFO bi = {};
        bi.bmiHeader.biSize = sizeof(bi.bmiHeader);
        bi.bmiHeader.biWidth = w;
        bi.bmiHeader.biHeight = -h;
        bi.bmiHeader.biPlanes = 1;
        bi.bmiHeader.biBitCount = 32;
        dc = CreateCompatibleDC(nullptr);
        bmp = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, (void**)&px, nullptr, 0);
        old = (HBITMAP)SelectObject(dc, bmp);
        Clear();
    }
    ~Canvas() { SelectObject(dc, old); DeleteObject(bmp); DeleteDC(dc); }
    void Clear() { for (int i = 0; i < w * h; i++) px[i] = 0x00FFFFFF; }
    unsigned At(int x, int y) const { return px[y * w + x] & 0x00FFFFFF; }
    void SaveBmp(const std::wstring& path) const {
        BITMAPFILEHEADER fh = {};
        BITMAPINFOHEADER ih = {};
        ih.biSize = sizeof(ih); ih.biWidth = w; ih.biHeight = -h; ih.biPlanes = 1; ih.biBitCount = 32;
        fh.bfType = 0x4D42;
        fh.bfOffBits = sizeof(fh) + sizeof(ih);
        fh.bfSize = fh.bfOffBits + w * h * 4;
        FILE* f = _wfopen(path.c_str(), L"wb");
        if (!f) return;
        fwrite(&fh, sizeof(fh), 1, f); fwrite(&ih, sizeof(ih), 1, f); fwrite(px, 4, w * h, f);
        fclose(f);
    }
};

struct Shape {
    int full = 0, partial = 0, outside = 0, wrongColour = 0;
    int minX = 9999, minY = 9999, maxX = -1, maxY = -1;  // bbox of fully covered pixels
};

// Analyses rect r on the canvas (background white, glyph #555555).
static Shape Analyse(const Canvas& c, RECT r) {
    Shape s;
    for (int y = 0; y < c.h; y++)
        for (int x = 0; x < c.w; x++) {
            unsigned v = c.At(x, y);
            bool inside = x >= r.left && x < r.right && y >= r.top && y < r.bottom;
            if (v == 0xFFFFFF) continue;
            if (!inside) { s.outside++; continue; }
            int rr = (v >> 16) & 255, gg = (v >> 8) & 255, bb = v & 255;
            if (rr != gg || gg != bb) s.wrongColour++;  // must be neutral grey
            if (v == 0x555555) {
                s.full++;
                s.minX = std::min(s.minX, x); s.maxX = std::max(s.maxX, x);
                s.minY = std::min(s.minY, y); s.maxY = std::max(s.maxY, y);
            } else if (rr < 0x55) {
                s.wrongColour++;  // darker than the glyph colour: not ours
            } else {
                s.partial++;
            }
        }
    return s;
}

static int Render(const std::wstring& outDir) {
    const int sizes[] = {9, 10, 12, 13, 14, 16, 18, 20, 24, 32};
    int failures = 0;
    Canvas sheet(2 * 40 + 8, (int)(sizeof(sizes) / sizeof(sizes[0])) * 40 + 8);
    int row = 0;
    for (int s : sizes) {
        for (int state = 1; state <= 2; state++) {
            Canvas c(s + 8, s + 8);
            RECT r = {4, 4, 4 + s, 4 + s};
            int rc = g_render(c.dc, state, &r);
            Shape a = Analyse(c, r);
            int bw = a.maxX - a.minX + 1, bh = a.maxY - a.minY + 1;
            bool orientOk = state == 1 ? (bh > bw) : (bw > bh);
            // Long side 8/9 of the box (even), short side half of it; allow AA rows.
            int L = 2 * (int)((s * 4.0 / 9.0) + 0.5); if (L < 4) L = 4;
            int longFull = state == 1 ? bh : bw;
            bool sizeOk = longFull >= L - 2 && longFull <= L;
            bool ok = rc == 0 && a.full > 0 && a.outside == 0 && a.wrongColour == 0 && orientOk && sizeOk;
            printf("size %2d %-8s full=%3d partial=%3d bbox=%dx%d long=%d (L=%d) outside=%d wrong=%d -> %s\n", s,
                   state == 1 ? "closed" : "opened", a.full, a.partial, bw, bh, longFull, L, a.outside, a.wrongColour,
                   ok ? "ok" : "FAIL");
            if (!ok) failures++;
            // Copy into the contact sheet (2x zoom when small for the preview).
            for (int y = 0; y < c.h; y++)
                for (int x = 0; x < c.w; x++)
                    sheet.px[(4 + row * 40 + y) * sheet.w + 4 + (state - 1) * 40 + x] = c.px[y * c.w + x];
        }
        row++;
    }
    if (!outDir.empty()) sheet.SaveBmp(outDir + L"\\glyphs.bmp");
    return failures ? Fail("glyph rendering checks") : 0;
}

static int WriteIni(const std::wstring& dir, const char* enabled) {
    std::wstring ini = dir + L"\\glyph.ini";
    FILE* f = _wfopen(ini.c_str(), L"wb");
    if (!f) return 1;
    fprintf(f, "[Glyph]\r\nEnabled=%s\r\nLog=1\r\n", enabled);
    fclose(f);
    return 0;
}

static int CountExact(const Canvas& c, unsigned colour) {
    int n = 0;
    for (int i = 0; i < c.w * c.h; i++) if ((c.px[i] & 0xFFFFFF) == colour) n++;
    return n;
}

static int Hook() {
    HTHEME h = OpenThemeData(nullptr, L"TREEVIEW");
    if (!h) { printf("SKIP: visual styles not available (OpenThemeData returned NULL)\n"); return 77; }
    Canvas c(24, 24);
    RECT r = {4, 4, 13, 13};
    // Replaced: both glyph parts, both states.
    for (int part : {TVP_GLYPH, TVP_HOTGLYPH})
        for (int state = 1; state <= 2; state++) {
            c.Clear();
            HRESULT hr = DrawThemeBackground(h, c.dc, part, state, &r, nullptr);
            int n = CountExact(c, 0x555555);
            printf("hooked DrawThemeBackground part=%d state=%d hr=0x%08lX grey=%d\n", part, state, (unsigned long)hr, n);
            if (FAILED(hr) || n < 6) { CloseThemeData(h); return Fail("glyph not replaced through uxtheme"); }
        }
    // Not replaced: an unknown state value.
    c.Clear();
    DrawThemeBackground(h, c.dc, TVP_GLYPH, 3, &r, nullptr);
    if (CountExact(c, 0x555555) >= 6) { CloseThemeData(h); return Fail("unknown glyph state was replaced"); }
    // DrawThemeBackgroundEx path.
    c.Clear();
    DTBGOPTS o = {sizeof(o), 0, {0, 0, 0, 0}};
    DrawThemeBackgroundEx(h, c.dc, TVP_GLYPH, GLPS_OPENED, &r, &o);
    if (CountExact(c, 0x555555) < 6) { CloseThemeData(h); return Fail("DrawThemeBackgroundEx not replaced"); }
    // Other theme classes are never touched.
    HTHEME b = OpenThemeData(nullptr, L"BUTTON");
    if (b) {
        c.Clear();
        DrawThemeBackground(b, c.dc, TVP_GLYPH, GLPS_CLOSED, &r, nullptr);
        if (CountExact(c, 0x555555) >= 6) { CloseThemeData(b); CloseThemeData(h); return Fail("non-TreeView theme was replaced"); }
        CloseThemeData(b);
    }
    // After shutdown Windows draws its own glyph again.
    g_shutdown();
    c.Clear();
    DrawThemeBackground(h, c.dc, TVP_GLYPH, GLPS_CLOSED, &r, nullptr);
    int after = CountExact(c, 0x555555);
    printf("after shutdown grey=%d\n", after);
    CloseThemeData(h);
    if (after >= 6) return Fail("glyph still replaced after shutdown");
    return 0;
}

static std::wstring TempCopy(const std::wstring& src, const wchar_t* name) {
    wchar_t tmp[MAX_PATH];
    GetTempPathW(MAX_PATH, tmp);
    std::wstring dir = std::wstring(tmp) + L"ekt-native-" + std::to_wstring(GetCurrentProcessId());
    CreateDirectoryW(dir.c_str(), nullptr);
    std::wstring dst = dir + L"\\" + name;
    CopyFileW(src.c_str(), dst.c_str(), FALSE);
    return dst;
}

static bool LogContains(const char* needle) {
    wchar_t local[MAX_PATH];
    GetEnvironmentVariableW(L"LOCALAPPDATA", local, MAX_PATH);
    std::wstring p = std::wstring(local) + L"\\EKTriangle\\Logs\\Native.log";
    FILE* f = _wfopen(p.c_str(), L"rb");
    if (!f) return false;
    std::string s;
    char buf[4096];
    size_t n;
    while ((n = fread(buf, 1, sizeof(buf), f)) > 0) s.append(buf, n);
    fclose(f);
    return s.find(needle) != std::string::npos;
}

int wmain(int argc, wchar_t** argv) {
    if (argc < 2) { printf("usage: mode [dll] [dir]\n"); return 2; }
    std::wstring mode = argv[1];
    if (mode == L"sleep") { Sleep(argc > 2 ? _wtoi(argv[2]) * 1000 : 600000); return 0; }
    if (argc < 3) return 2;
    std::wstring dll = argv[2];
    std::wstring dir = argc > 3 ? argv[3] : L".";

    if (mode == L"legacy") {
        // A module named EKGlyphHook.dll (the experimental hook) is already loaded.
        if (!LoadLibraryW(TempCopy(dll, L"EKGlyphHook.dll").c_str())) return Fail("could not load stand-in legacy DLL");
    }
    if (!Load(dll.c_str())) return Fail("DLL or exports missing");
    if (mode == L"exports") return 0;
    if (mode == L"render") return Render(dir);

    if (WriteIni(dir, mode == L"disabled" ? "0" : "1")) return Fail("cannot write glyph.ini");
    int st = g_init(dir.c_str());
    printf("EKTriangle_Init -> %d\n", st);
    if (mode == L"notrevit") return st == 2 ? 0 : Fail("hook must refuse non-Revit hosts");
    if (mode == L"disabled") return st == 1 ? 0 : Fail("Enabled=0 must not hook");
    if (mode == L"legacy") return st == 3 ? 0 : Fail("must stay inactive next to legacy EKGlyphHook.dll");
    if (mode == L"dup") {
        if (st != 0) return Fail("first copy should activate");
        HMODULE second = LoadLibraryW(TempCopy(dll, L"EKTriangleHook.dll").c_str());
        InitFn init2 = second ? (InitFn)(void*)GetProcAddress(second, "EKTriangle_Init") : nullptr;
        if (!init2 || second == g_dll) return Fail("could not load a second copy");
        int st2 = init2(dir.c_str());
        printf("second copy EKTriangle_Init -> %d\n", st2);
        return st2 == 3 ? 0 : Fail("second copy must stay inactive");
    }
    if (mode == L"hook") {
        if (st != 0) return Fail("hook should activate in Revit.exe");
        if (!LogContains("hooks active")) return Fail("Native.log missing startup line");
        if (g_init(dir.c_str()) != 0) return Fail("re-init must be harmless");
        return Hook();
    }
    return Fail("unknown mode");
}
