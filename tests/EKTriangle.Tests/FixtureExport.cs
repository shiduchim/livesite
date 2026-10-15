using System;
using System.IO;
using Xunit;

namespace EKTriangle.Tests
{
    /// <summary>Writes the synthetic main.js files used by the Windows end-to-end installer test.</summary>
    public class FixtureExport
    {
        [Fact]
        public void Export_fixtures_when_requested()
        {
            string dir = Environment.GetEnvironmentVariable("EKT_FIXTURE_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "main-original.js"), Js.Bytes(Js.Original(1)));
            File.WriteAllBytes(Path.Combine(dir, "main-unsupported.js"), Js.Bytes(Js.Original(2, minus: "")));
            File.WriteAllBytes(Path.Combine(dir, "main-original-bom.js"), Js.Bytes(Js.Original(4), bom: true));
            File.WriteAllBytes(Path.Combine(dir, "main-autodesk-update.js"), Js.Bytes(Js.Original(99)));
        }
    }
}
