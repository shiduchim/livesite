using System;
using System.Collections.Generic;

namespace EKTriangle.Core
{
    /// <summary>Fixed product identity. Changing these values breaks upgrades and ownership checks.</summary>
    public static class Product
    {
        public const string Name = "EK Triangle";
        public const string Version = "1.0.0";

        /// <summary>Revit AddInId of the release add-in (new; deliberately different from the legacy test).</summary>
        public const string AddInId = "D24DC5EF-6D2B-4013-B5D0-37B8F2763892";
        public const string ManifestFileName = "EKTriangle.addin";
        public const string LoaderFileName = "EKTriangle.dll";
        public const string NativeFileName = "EKTriangleHook.dll";
        public const string LoaderClass = "EKTriangle.Loader.App";

        /// <summary>AddInId used by the EKGlyph / EKTreeGlyphProbe2024 experimental add-in.</summary>
        public const string LegacyAddInId = "6F0C2E7B-6B0E-4C58-9E1B-3C1A3D9F2E41";
        public const string LegacyClass = "EKGlyph.App";

        /// <summary>Identifies the Project Browser modification written by this release.</summary>
        public const string BrowserPatchId = "EKTriangle.Browser.v1";

        public static readonly IReadOnlyList<int> SupportedYears = new[] { 2024, 2025, 2026, 2027 };

        public static bool IsSupportedYear(int year)
        {
            foreach (int y in SupportedYears) if (y == year) return true;
            return false;
        }
    }
}
