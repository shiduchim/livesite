using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EKTriangle.Core;

namespace EKTriangle.Tests
{
    /// <summary>
    /// Builds realistic stand-ins for Revit's minified Project Browser bundle.
    /// The collapsed-icon definition is copied verbatim from the Revit 2024
    /// main.js excerpt in legacy-reference/ProjectBrowser_SVG_Notes.txt; the
    /// expanded icon and Add button use the same compiled SVGR shape.
    /// </summary>
    public static class Js
    {
        public const string PlusDef =
            "var Po,Go=function(e){return o.createElement(\"svg\",Ho({width:16,height:16},e),qo||(qo=o.createElement(\"path\",{d:\"M7 7V4h2v3h3v2H9v3H7V9H4V7h3z\"})))};";
        public const string MinusDef =
            "var Ko,Xo=function(e){return o.createElement(\"svg\",Yo({width:16,height:16},e),Ko||(Ko=o.createElement(\"path\",{d:\"M4 7h8v2H4z\"})))};";
        public const string AddDef =
            "var Zo,Jo=function(e){return o.createElement(\"svg\",Qo({width:16,height:16},e),Zo||(Zo=o.createElement(\"path\",{d:\"M7 7V2h2v5h5v2H9v5H7V9H2V7h5z\"})))};";
        public const string MultiPathDef =
            "var ea,ta=function(e){return o.createElement(\"svg\",na({width:16,height:16},e),ea||(ea=o.createElement(\"path\",{d:\"M1 1h14v2H1z\"})),o.createElement(\"path\",{d:\"M1 5h14v2H1z\"}))};";
        public const string Toggle =
            "function ra(e){var t=e.expanded;return o.createElement(\"span\",{className:\"pb-tree-toggle\",onClick:e.onToggle},t?o.createElement(Xo,null):o.createElement(Go,null))}";
        public const string Toolbar =
            "function ia(e){return o.createElement(\"button\",{className:\"pb-add\",title:\"Add \\u00e9l\\u00e9ment\"},o.createElement(Jo,null))}";

        static string Filler(int seed, int approxBytes)
        {
            var sb = new StringBuilder();
            var rnd = new Random(seed);
            int i = 0;
            while (sb.Length < approxBytes)
            {
                sb.Append("var a").Append(i).Append("=function(e,t,n){\"use strict\";n.d(t,\"a\",function(){return r").Append(rnd.Next(1000))
                  .Append("});var r=n(").Append(rnd.Next(500)).Append("),l=\"Vue / Ansicht \u00e4\u00f6\u00fc \u65e5\u672c\u8a9e\";return r.createElement(\"div\",{className:\"pb-").Append(i).Append("\"},l)};\n");
                i++;
            }
            return sb.ToString();
        }

        public static string Original(int seed = 1, string plus = PlusDef, string minus = MinusDef, string extra = "")
        {
            return "/*! For license information please see main.js.LICENSE.txt */\n" +
                   Filler(seed, 120_000) + plus + Filler(seed + 1, 2_000) + minus + AddDef + MultiPathDef + extra +
                   Filler(seed + 2, 60_000) + Toggle + Toolbar + Filler(seed + 3, 120_000);
        }

        public static byte[] Bytes(string text, bool bom = false)
        {
            var body = new UTF8Encoding(false).GetBytes(text);
            return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
        }
    }

    /// <summary>A throw-away directory tree that looks like a Windows PC with Revit installed.</summary>
    public sealed class Machine : IDisposable
    {
        public readonly string Root;
        public readonly Env Env;
        public readonly List<RunningRevit> Running = new List<RunningRevit>();
        public bool Admin = true;
        public readonly Dictionary<int, string> CustomDirs = new Dictionary<int, string>();

        public Machine()
        {
            Root = Path.Combine(Path.GetTempPath(), "ektri-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            Env = new Env
            {
                ProgramFiles = Path.Combine(Root, "Program Files"),
                ProgramData = Path.Combine(Root, "ProgramData"),
                AppData = Path.Combine(Root, "Users", "u", "AppData", "Roaming"),
                LocalAppData = Path.Combine(Root, "Users", "u", "AppData", "Local"),
            };
            Env.RevitProcesses = () => Running.ToArray();
            Env.IsAdmin = () => Admin;
            Env.ExtraRevitDirs = y => CustomDirs.TryGetValue(y, out var d) ? new[] { d } : new string[0];
            Directory.CreateDirectory(Env.ProgramFiles);
            Directory.CreateDirectory(Env.ProgramData);
            Directory.CreateDirectory(Env.AppData);
            Directory.CreateDirectory(Env.LocalAppData);
        }

        public string InstallRevit(int year, byte[] mainJs, string dir = null)
        {
            dir ??= Env.DefaultRevitDir(year);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Revit.exe"), "MZ fake Revit " + year);
            File.WriteAllText(Path.Combine(dir, "RevitAPI.dll"), "fake");
            if (mainJs != null)
            {
                string js = Env.BrowserScript(dir);
                Directory.CreateDirectory(Path.GetDirectoryName(js));
                File.WriteAllBytes(js, mainJs);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(js), "vendor.js"), "/* other Autodesk file */");
            }
            // Unrelated add-ins that must never change.
            string addins = Env.UserAddinsDir(year);
            Directory.CreateDirectory(Path.Combine(addins, "OtherVendor"));
            File.WriteAllText(Path.Combine(addins, "OtherVendor.addin"),
                "<?xml version=\"1.0\"?><RevitAddIns><AddIn Type=\"Application\"><Name>Other</Name><Assembly>OtherVendor\\Other.dll</Assembly>" +
                "<FullClassName>Other.App</FullClassName><AddInId>11111111-2222-3333-4444-555555555555</AddInId><VendorId>OTHR</VendorId></AddIn></RevitAddIns>");
            File.WriteAllText(Path.Combine(addins, "OtherVendor", "Other.dll"), "other");
            File.WriteAllText(Path.Combine(addins, "EKHDsnip.addin"),
                "<?xml version=\"1.0\"?><RevitAddIns><AddIn Type=\"Command\"><Name>EK HDsnip</Name><Assembly>EKHDsnip\\EKHDsnip.dll</Assembly>" +
                "<FullClassName>EKHDsnip.Cmd</FullClassName><AddInId>22222222-2222-3333-4444-555555555555</AddInId><VendorId>EKHD</VendorId></AddIn></RevitAddIns>");
            return dir;
        }

        public string MainJs(int year) => Env.BrowserScript(Env.FindRevitDir(year));

        /// <summary>The experimental add-in exactly as tools\glyph-setup.ps1 of the 2024 test package installed it.</summary>
        public (string manifest, string folder) InstallLegacyProbe(int year = 2024)
        {
            string dir = Env.UserAddinsDir(year);
            string folder = Path.Combine(dir, "EKTreeGlyphProbe2024");
            Directory.CreateDirectory(folder);
            foreach (var f in new[] { "EKGlyph.dll", "EKGlyphHook.dll", "glyph.ini" }) File.WriteAllText(Path.Combine(folder, f), "legacy " + f);
            File.WriteAllText(Path.Combine(folder, "EKTreeGlyphProbe2024.owner"), "Created by EKTreeGlyphProbe2024 isolated installer");
            string manifest = Path.Combine(dir, "EKTreeGlyphProbe2024.addin");
            File.WriteAllText(manifest, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<RevitAddIns><AddIn Type=\"Application\"><Name>EK Tree Glyph Probe 2024</Name><Assembly>" +
                Path.Combine(folder, "EKGlyph.dll") + "</Assembly><FullClassName>EKGlyph.App</FullClassName><AddInId>6F0C2E7B-6B0E-4C58-9E1B-3C1A3D9F2E41</AddInId>" +
                "<VendorId>EKHD</VendorId><VendorDescription>Temporary reversible test of a tree glyph painter</VendorDescription></AddIn></RevitAddIns>");
            return (manifest, folder);
        }

        /// <summary>A setup payload folder like {app} after Inno copied the files.</summary>
        public string Payload()
        {
            string app = Path.Combine(Env.LocalAppData, "Programs", "EK Triangle");
            foreach (int y in Product.SupportedYears)
            {
                string d = Path.Combine(app, "Revit" + y);
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, Product.LoaderFileName), "loader " + y);
                File.WriteAllText(Path.Combine(d, Product.NativeFileName), "native");
            }
            File.WriteAllText(Path.Combine(app, "glyph.ini"), "[Glyph]\r\nEnabled=1\r\n");
            return app;
        }

        /// <summary>SHA-256 of every file below a folder, excluding paths that contain any of the given fragments.</summary>
        public Dictionary<string, string> Snapshot(string dir, params string[] exclude)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dir)) return d;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (exclude.Any(x => f.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                d[f.Substring(dir.Length)] = FileUtil.Sha256File(f);
            }
            return d;
        }

        public void Dispose()
        {
            TestHooks.BeforeReplace = null;
            TestHooks.AfterReplace = null;
            try
            {
                foreach (var f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Root, true);
            }
            catch { }
        }
    }
}
