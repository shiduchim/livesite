using System;
using System.Collections.Generic;
using System.Security.Principal;
using Microsoft.Win32;

namespace EKTriangle.Core
{
    /// <summary>Windows-only lookups. Every member is safe to call on other platforms (returns nothing).</summary>
    public static class WindowsRegistry
    {
        public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>InstallLocation values of "Revit YYYY" / "Autodesk Revit YYYY" uninstall entries.</summary>
        public static IEnumerable<string> RevitInstallLocations(int year)
        {
            var found = new List<string>();
            if (!IsWindows) return found;
            string[] names = { "Revit " + year, "Autodesk Revit " + year };
            const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var root = hklm.OpenSubKey(key))
                {
                    if (root == null) return found;
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = root.OpenSubKey(sub))
                            {
                                var display = (k?.GetValue("DisplayName") as string ?? "").Trim();
                                var loc = k?.GetValue("InstallLocation") as string;
                                if (string.IsNullOrWhiteSpace(loc)) continue;
                                foreach (var n in names)
                                    if (string.Equals(display, n, StringComparison.OrdinalIgnoreCase))
                                        found.Add(loc.Trim().TrimEnd('\\'));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return found;
        }

        public static bool IsAdministrator()
        {
            if (!IsWindows) return true;
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
