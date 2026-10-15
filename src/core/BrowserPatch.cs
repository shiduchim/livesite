using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace EKTriangle.Core
{
    public enum BrowserState
    {
        /// <summary>Autodesk original: both tree-toggle icons recognised, no EK content.</summary>
        Original,
        /// <summary>EK Triangle modification present, exactly as written by this product.</summary>
        Patched,
        /// <summary>The earlier EK SymbolSwap "T" modification is present (convertible).</summary>
        LegacyT,
        /// <summary>Not recognised / ambiguous: must not be modified.</summary>
        Unsupported,
    }

    public sealed class BrowserAnalysis
    {
        public BrowserState State;
        public string Reason = "";
        public int CollapsedCount, ExpandedCount, AddCount, EkCollapsedCount, EkExpandedCount, LegacyTCount, SvgCount;
        public string CollapsedComponent, ExpandedComponent;
        public bool TogglePairSeen;

        public bool CanApply => State == BrowserState.Original || State == BrowserState.LegacyT;
    }

    /// <summary>
    /// Recognition and exact text transformation of the Project Browser's
    /// tree expand/collapse icons in AddIns\ProjectBrowser\...\main.js.
    ///
    /// The browser draws each icon with a React SVG component compiled to
    ///   X=function(e){return o.createElement("svg",H({width:16,height:16},e),
    ///                 c||(c=o.createElement("path",{d:"..."})))}
    /// Collapsed nodes use the small 8 px plus  (M7 7V4h2v3h3v2H9v3H7V9H4V7h3z),
    /// expanded nodes use the matching 8 px minus (M4 7h8v2H4z). The toolbar
    /// "Add" button uses a different, 12 px plus (M7 7V2h2v5h5v2H9v5H7V9H2V7h5z)
    /// which is never touched and is checked to be unchanged.
    ///
    /// Only the `d:"..."` property of the two recognised path elements is
    /// replaced. Each literal must occur exactly once in the file and sit in a
    /// single-path 16x16 SVG element, otherwise the file is reported as
    /// unsupported and left alone.
    /// </summary>
    public static class BrowserPatch
    {
        public const string CollapsedOriginal = "d:\"M7 7V4h2v3h3v2H9v3H7V9H4V7h3z\"";
        public const string ExpandedOriginal = "d:\"M4 7h8v2H4z\"";
        public const string AddButton = "d:\"M7 7V2h2v5h5v2H9v5H7V9H2V7h5z\"";
        public const string LegacyT = "d:\"M7 3h2v5h4v2H3V8h4z\"";

        // Triangles in the same 16x16 box: long side 8 px, short side 4 px,
        // 45-degree edges, straight edge on whole pixels. Inline style beats
        // any stylesheet fill so the colour is exactly #555555.
        public const string CollapsedEk = "d:\"M6 4L10 8L6 12Z\",style:{fill:\"#555555\"}";
        public const string ExpandedEk = "d:\"M4 6L12 6L8 10Z\",style:{fill:\"#555555\"}";

        const string PathOpen = "createElement(\"path\",{";
        const string PathClose = "})";
        const string SvgOpen = "createElement(\"svg\"";
        const int Window = 400;

        public static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

        // ---------------------------------------------------------- decoding --

        public sealed class Decoded
        {
            public bool HasBom;
            public string Text;
        }

        /// <summary>Strict UTF-8 decode that must round-trip to identical bytes.</summary>
        public static Decoded Decode(byte[] bytes)
        {
            bool bom = bytes.Length >= 3 && bytes[0] == Bom[0] && bytes[1] == Bom[1] && bytes[2] == Bom[2];
            int offset = bom ? 3 : 0;
            string text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            var back = Encode(new Decoded { HasBom = bom, Text = text });
            if (back.Length != bytes.Length) throw new FormatException("File does not round-trip as UTF-8.");
            for (int i = 0; i < back.Length; i++)
                if (back[i] != bytes[i]) throw new FormatException("File does not round-trip as UTF-8.");
            return new Decoded { HasBom = bom, Text = text };
        }

        public static byte[] Encode(Decoded d)
        {
            byte[] body = StrictUtf8.GetBytes(d.Text);
            if (!d.HasBom) return body;
            var all = new byte[body.Length + 3];
            Buffer.BlockCopy(Bom, 0, all, 0, 3);
            Buffer.BlockCopy(body, 0, all, 3, body.Length);
            return all;
        }

        // ---------------------------------------------------------- analysis --

        public static int Count(string text, string token)
        {
            int n = 0, at = 0;
            while ((at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0) { n++; at += token.Length; }
            return n;
        }

        static readonly Regex ComponentHead = new Regex(
            @"([A-Za-z_$][\w$]*)\s*=\s*function\s*\(\s*[A-Za-z_$][\w$]*\s*\)\s*\{\s*return\s+[A-Za-z_$][\w$]*\.$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Checks that the literal at <paramref name="at"/> is the d property of
        /// the only path in a 16x16 SVG element. Returns null on success or the reason.
        /// </summary>
        static string CheckSite(string text, int at, string literal, out string component)
        {
            component = null;
            if (at < PathOpen.Length || string.CompareOrdinal(text, at - PathOpen.Length, PathOpen, 0, PathOpen.Length) != 0)
                return "is not the d property of an SVG path element";
            int after = at + literal.Length;
            if (string.CompareOrdinal(text, after, PathClose, 0, PathClose.Length) != 0)
                return "path element has unexpected extra properties";
            int from = Math.Max(0, at - Window);
            int svg = text.LastIndexOf(SvgOpen, at, at - from, StringComparison.Ordinal);
            if (svg < 0) return "no enclosing SVG element found";
            string head = text.Substring(svg, at - svg);
            if (!Regex.IsMatch(head, @"\{\s*width\s*:\s*16\s*,\s*height\s*:\s*16\s*\}"))
                return "enclosing SVG element is not 16x16";
            if (Count(head, "createElement(\"path\"") != 1)
                return "enclosing SVG element draws more than one path";
            int headStart = Math.Max(0, svg - 120);
            var m = ComponentHead.Match(text.Substring(headStart, svg - headStart));
            if (m.Success) component = m.Groups[1].Value;
            return null;
        }

        public static BrowserAnalysis Analyze(string text)
        {
            var a = new BrowserAnalysis
            {
                CollapsedCount = Count(text, CollapsedOriginal),
                ExpandedCount = Count(text, ExpandedOriginal),
                AddCount = Count(text, AddButton),
                EkCollapsedCount = Count(text, CollapsedEk),
                EkExpandedCount = Count(text, ExpandedEk),
                LegacyTCount = Count(text, LegacyT),
                SvgCount = Count(text, SvgOpen),
            };
            // A partial EK literal (e.g. edited by hand) is never ours to touch.
            int ekPathOnlyC = Count(text, "d:\"M6 4L10 8L6 12Z\"");
            int ekPathOnlyE = Count(text, "d:\"M4 6L12 6L8 10Z\"");
            if (ekPathOnlyC != a.EkCollapsedCount || ekPathOnlyE != a.EkExpandedCount)
                return Fail(a, "contains an altered EK Triangle icon definition");

            string collapsedLit, expandedLit;
            if (a.EkCollapsedCount == 0 && a.EkExpandedCount == 0 && a.LegacyTCount == 0)
            {
                a.State = BrowserState.Original;
                collapsedLit = CollapsedOriginal; expandedLit = ExpandedOriginal;
                if (a.CollapsedCount != 1) return Fail(a, a.CollapsedCount == 0
                    ? "collapsed-node icon (small plus) not found"
                    : "collapsed-node icon (small plus) occurs " + a.CollapsedCount + " times");
                if (a.ExpandedCount != 1) return Fail(a, a.ExpandedCount == 0
                    ? "expanded-node icon (small minus) not found"
                    : "expanded-node icon (small minus) occurs " + a.ExpandedCount + " times");
            }
            else if (a.EkCollapsedCount == 1 && a.EkExpandedCount == 1 && a.CollapsedCount == 0 &&
                     a.ExpandedCount == 0 && a.LegacyTCount == 0)
            {
                a.State = BrowserState.Patched;
                collapsedLit = CollapsedEk; expandedLit = ExpandedEk;
            }
            else if (a.LegacyTCount == 1 && a.CollapsedCount == 0 && a.ExpandedCount == 1 &&
                     a.EkCollapsedCount == 0 && a.EkExpandedCount == 0)
            {
                a.State = BrowserState.LegacyT;
                collapsedLit = LegacyT; expandedLit = ExpandedOriginal;
            }
            else
            {
                return Fail(a, string.Format(
                    "icon definitions in an unexpected combination (plus={0}, minus={1}, EK={2}/{3}, T={4})",
                    a.CollapsedCount, a.ExpandedCount, a.EkCollapsedCount, a.EkExpandedCount, a.LegacyTCount));
            }

            int ci = text.IndexOf(collapsedLit, StringComparison.Ordinal);
            int ei = text.IndexOf(expandedLit, StringComparison.Ordinal);
            string why = CheckSite(text, ci, collapsedLit, out a.CollapsedComponent);
            if (why != null) return Fail(a, "collapsed-node icon " + why);
            why = CheckSite(text, ei, expandedLit, out a.ExpandedComponent);
            if (why != null) return Fail(a, "expanded-node icon " + why);
            if (a.CollapsedComponent != null && a.CollapsedComponent == a.ExpandedComponent)
                return Fail(a, "both icons resolve to the same component");
            a.TogglePairSeen = TogglePair(text, a.CollapsedComponent, a.ExpandedComponent);
            a.Reason = a.State == BrowserState.Original ? "both tree icons recognised"
                     : a.State == BrowserState.Patched ? "EK Triangle icons present"
                     : "earlier EK SymbolSwap T icon present";
            return a;
        }

        static BrowserAnalysis Fail(BrowserAnalysis a, string reason)
        {
            a.State = BrowserState.Unsupported;
            a.Reason = reason;
            return a;
        }

        /// <summary>Informational: both components rendered next to each other (e.g. in a ternary).</summary>
        static bool TogglePair(string text, string c, string e)
        {
            if (c == null || e == null) return false;
            var re = new Regex(@"createElement\(" + Regex.Escape(c) + @"[,)]", RegexOptions.CultureInvariant);
            foreach (Match m in re.Matches(text))
            {
                int from = Math.Max(0, m.Index - 300);
                int len = Math.Min(text.Length - from, 600);
                if (Regex.IsMatch(text.Substring(from, len), @"createElement\(" + Regex.Escape(e) + @"[,)]"))
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------- transformation --

        static string ReplaceUnique(string text, string from, string to)
        {
            int at = text.IndexOf(from, StringComparison.Ordinal);
            if (at < 0 || text.IndexOf(from, at + from.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Expected exactly one occurrence of " + from);
            return text.Substring(0, at) + to + text.Substring(at + from.Length);
        }

        /// <summary>Original -> EK triangles. Throws unless every check passes.</summary>
        public static string Apply(string original)
        {
            var before = Analyze(original);
            if (before.State != BrowserState.Original) throw new InvalidOperationException("Not an original file: " + before.Reason);
            string t = ReplaceUnique(original, CollapsedOriginal, CollapsedEk);
            t = ReplaceUnique(t, ExpandedOriginal, ExpandedEk);
            var after = Analyze(t);
            if (after.State != BrowserState.Patched) throw new InvalidOperationException("Result did not verify: " + after.Reason);
            if (after.AddCount != before.AddCount || after.SvgCount != before.SvgCount)
                throw new InvalidOperationException("Result changed unrelated icons.");
            int delta = (CollapsedEk.Length - CollapsedOriginal.Length) + (ExpandedEk.Length - ExpandedOriginal.Length);
            if (t.Length - original.Length != delta) throw new InvalidOperationException("Unexpected size change.");
            if (Reverse(t) != original) throw new InvalidOperationException("Transformation is not exactly reversible.");
            return t;
        }

        /// <summary>EK triangles -> original. Removes only the two EK literals.</summary>
        public static string Reverse(string patched)
        {
            var before = Analyze(patched);
            if (before.State != BrowserState.Patched) throw new InvalidOperationException("No EK Triangle icons to remove: " + before.Reason);
            string t = ReplaceUnique(patched, CollapsedEk, CollapsedOriginal);
            t = ReplaceUnique(t, ExpandedEk, ExpandedOriginal);
            var after = Analyze(t);
            if (after.State != BrowserState.Original) throw new InvalidOperationException("Restored text did not verify: " + after.Reason);
            if (after.AddCount != before.AddCount || after.SvgCount != before.SvgCount)
                throw new InvalidOperationException("Restore changed unrelated icons.");
            return t;
        }

        /// <summary>EK SymbolSwap "T" -> original plus.</summary>
        public static string FromLegacyT(string text)
        {
            var before = Analyze(text);
            if (before.State != BrowserState.LegacyT) throw new InvalidOperationException("No legacy T icon: " + before.Reason);
            string t = ReplaceUnique(text, LegacyT, CollapsedOriginal);
            var after = Analyze(t);
            if (after.State != BrowserState.Original) throw new InvalidOperationException("Legacy conversion did not verify: " + after.Reason);
            return t;
        }
    }

    /// <summary>Original main.js builds that were inspected and confirmed by the user.</summary>
    public static class KnownBuilds
    {
        static readonly Dictionary<string, string> Approved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Revit 2024 main.js inspected for EK SymbolSwap v1.1 and patched successfully on the user's PC.
            { "9CFAC3DA7225DA91039D30FCA37EAC589575450527EE00BC1503236B78841A39", "Revit 2024 (user-verified build)" },
        };

        public static string Describe(string originalSha256)
        {
            string d;
            return originalSha256 != null && Approved.TryGetValue(originalSha256, out d) ? d : null;
        }
    }
}
