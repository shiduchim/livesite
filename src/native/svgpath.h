// Minimal SVG path-data parser (M L H V C S Q T A Z, absolute and relative).
// Output: closed/open figures made of cubic Bezier segments, so the same data
// can be filled with GDI+ (or rasterised in tests) without any SVG library.
#pragma once
#include <cctype>
#include <cmath>
#include <cstdlib>
#include <string>
#include <vector>

namespace svg {

struct Pt { double x, y; };

// pts[0] is the start point; each following triple is (c1, c2, end).
struct Figure {
    std::vector<Pt> pts;
    bool closed = false;
};

class Parser {
public:
    explicit Parser(const std::string& d) : s_(d) {}

    bool parse(std::vector<Figure>& out) {
        char cmd = 0;
        Pt cur{0, 0}, start{0, 0}, lastCtl{0, 0};
        char prev = 0;
        Figure fig;
        auto flush = [&] {
            if (fig.pts.size() > 1) out.push_back(fig);
            fig = Figure();
        };
        auto lineTo = [&](Pt p) {
            if (fig.pts.empty()) fig.pts.push_back(cur);
            fig.pts.push_back(cur);
            fig.pts.push_back(p);
            fig.pts.push_back(p);
            cur = p;
        };
        auto cubicTo = [&](Pt c1, Pt c2, Pt p) {
            if (fig.pts.empty()) fig.pts.push_back(cur);
            fig.pts.push_back(c1);
            fig.pts.push_back(c2);
            fig.pts.push_back(p);
            cur = p;
        };
        while (true) {
            skipWs();
            if (i_ >= s_.size()) break;
            char c = s_[i_];
            if (std::isalpha(static_cast<unsigned char>(c))) {
                cmd = c;
                ++i_;
            } else if (!cmd) {
                return false;
            }
            bool rel = std::islower(static_cast<unsigned char>(cmd)) != 0;
            char up = static_cast<char>(std::toupper(static_cast<unsigned char>(cmd)));
            Pt base = rel ? cur : Pt{0, 0};
            double a, b, c1x, c1y, c2x, c2y;
            switch (up) {
            case 'M':
                if (!num(a) || !num(b)) return false;
                flush();
                cur = {base.x + a, base.y + b};
                start = cur;
                fig.pts.push_back(cur);
                cmd = rel ? 'l' : 'L';  // subsequent pairs are implicit line-tos
                break;
            case 'L':
                if (!num(a) || !num(b)) return false;
                lineTo({base.x + a, base.y + b});
                break;
            case 'H':
                if (!num(a)) return false;
                lineTo({base.x + a, cur.y});
                break;
            case 'V':
                if (!num(a)) return false;
                lineTo({cur.x, (rel ? cur.y : 0) + a});
                break;
            case 'C':
                if (!num(c1x) || !num(c1y) || !num(c2x) || !num(c2y) || !num(a) || !num(b)) return false;
                lastCtl = {base.x + c2x, base.y + c2y};
                cubicTo({base.x + c1x, base.y + c1y}, lastCtl, {base.x + a, base.y + b});
                break;
            case 'S': {
                if (!num(c2x) || !num(c2y) || !num(a) || !num(b)) return false;
                Pt r = (prev == 'C' || prev == 'S') ? Pt{2 * cur.x - lastCtl.x, 2 * cur.y - lastCtl.y} : cur;
                lastCtl = {base.x + c2x, base.y + c2y};
                cubicTo(r, lastCtl, {base.x + a, base.y + b});
                break;
            }
            case 'Q':
                if (!num(c1x) || !num(c1y) || !num(a) || !num(b)) return false;
                lastCtl = {base.x + c1x, base.y + c1y};
                quadTo(lastCtl, {base.x + a, base.y + b}, cur, cubicTo);
                break;
            case 'T': {
                if (!num(a) || !num(b)) return false;
                lastCtl = (prev == 'Q' || prev == 'T') ? Pt{2 * cur.x - lastCtl.x, 2 * cur.y - lastCtl.y} : cur;
                quadTo(lastCtl, {base.x + a, base.y + b}, cur, cubicTo);
                break;
            }
            case 'A': {
                double rx, ry, rot, large, sweep;
                if (!num(rx) || !num(ry) || !num(rot) || !flag(large) || !flag(sweep) || !num(a) || !num(b)) return false;
                arcTo(rx, ry, rot, large != 0, sweep != 0, {base.x + a, base.y + b}, cur, lineTo, cubicTo);
                break;
            }
            case 'Z':
                if (!fig.pts.empty()) {
                    if (cur.x != start.x || cur.y != start.y) lineTo(start);
                    fig.closed = true;
                    flush();
                }
                cur = start;
                break;
            default:
                return false;
            }
            prev = up;
        }
        flush();
        return !out.empty();
    }

private:
    template <class F>
    static void quadTo(Pt q, Pt p, Pt cur, F cubicTo) {
        cubicTo({cur.x + 2.0 / 3 * (q.x - cur.x), cur.y + 2.0 / 3 * (q.y - cur.y)},
                {p.x + 2.0 / 3 * (q.x - p.x), p.y + 2.0 / 3 * (q.y - p.y)}, p);
    }

    // SVG endpoint arc -> centre parameterisation -> cubic segments (<= 90 deg each).
    template <class L, class C>
    static void arcTo(double rx, double ry, double rotDeg, bool large, bool sweep, Pt p, Pt cur, L lineTo, C cubicTo) {
        const double PI = 3.14159265358979323846;
        if ((p.x == cur.x && p.y == cur.y)) return;
        rx = std::fabs(rx);
        ry = std::fabs(ry);
        if (rx == 0 || ry == 0) { lineTo(p); return; }
        double phi = rotDeg * PI / 180, cs = std::cos(phi), sn = std::sin(phi);
        double dx = (cur.x - p.x) / 2, dy = (cur.y - p.y) / 2;
        double x1 = cs * dx + sn * dy, y1 = -sn * dx + cs * dy;
        double lam = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry);
        if (lam > 1) { rx *= std::sqrt(lam); ry *= std::sqrt(lam); }
        double num_ = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double k = std::sqrt(std::fmax(0.0, num_ / den)) * (large == sweep ? -1 : 1);
        double cxp = k * rx * y1 / ry, cyp = -k * ry * x1 / rx;
        double cx = cs * cxp - sn * cyp + (cur.x + p.x) / 2;
        double cy = sn * cxp + cs * cyp + (cur.y + p.y) / 2;
        auto ang = [](double ux, double uy, double vx, double vy) {
            return std::atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        };
        double t1 = ang(1, 0, (x1 - cxp) / rx, (y1 - cyp) / ry);
        double dt = ang((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry);
        if (!sweep && dt > 0) dt -= 2 * PI;
        if (sweep && dt < 0) dt += 2 * PI;
        int n = static_cast<int>(std::ceil(std::fabs(dt) / (PI / 2) - 1e-9));
        if (n < 1) n = 1;
        double step = dt / n, kk = 4.0 / 3 * std::tan(step / 4);
        auto at = [&](double t, double ox, double oy) {
            double ex = rx * (std::cos(t) - ox * std::sin(t)), ey = ry * (std::sin(t) + oy * std::cos(t));
            return Pt{cx + cs * ex - sn * ey, cy + sn * ex + cs * ey};
        };
        for (int j = 0; j < n; ++j) {
            double a0 = t1 + j * step, a1 = a0 + step;
            Pt c1 = at(a0, kk, kk), c2 = at(a1, -kk, -kk);
            Pt e = (j == n - 1) ? p : at(a1, 0, 0);
            cubicTo(c1, c2, e);
        }
    }

    void skipWs() {
        while (i_ < s_.size() && (std::isspace(static_cast<unsigned char>(s_[i_])) || s_[i_] == ',')) ++i_;
    }
    bool num(double& v) {
        skipWs();
        if (i_ >= s_.size()) return false;
        const char* b = s_.c_str() + i_;
        char* e = nullptr;
        v = std::strtod(b, &e);
        if (e == b) return false;
        i_ += static_cast<size_t>(e - b);
        return true;
    }
    // Arc flags may be written without separators ("a1 1 0 011 1").
    bool flag(double& v) {
        skipWs();
        if (i_ >= s_.size() || (s_[i_] != '0' && s_[i_] != '1')) return false;
        v = s_[i_++] - '0';
        return true;
    }

    std::string s_;
    size_t i_ = 0;
};

inline bool Parse(const std::string& d, std::vector<Figure>& out) {
    out.clear();
    return Parser(d).parse(out);
}

}  // namespace svg
