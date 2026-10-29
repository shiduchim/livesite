using Offline3D;
using Offline3D.Automatic;

namespace EKFastPDF.Tests;

/// <summary>Finding a registration marker's L corner among the paths in its colour.</summary>
public class RegistrationMarkerTests
{
    private static Point3 P(double x, double y) => new(x, y);

    [Fact]
    public void TwoLines_SharingACornerAtRightAngles()
    {
        var (corner, paths, _) = AutomaticRegistrationService.MarkerCorner(new[]
        {
            new[] { P(1024.09, 742.404), P(1029.849, 742.404) },
            new[] { P(1024.09, 742.404), P(1024.09, 746.244) },
        });
        Assert.NotNull(corner);
        Assert.Equal(1024.09, corner!.Value.X, 3); Assert.Equal(742.404, corner.Value.Y, 3);
        Assert.Equal(new[] { 0, 1 }, paths);
    }

    [Fact]
    public void OnePolyline_WithTheThreeLPoints()
    {
        var (corner, paths, _) = AutomaticRegistrationService.MarkerCorner(new[] { new[] { P(10, 0), P(0, 0), P(0, 5) } });
        Assert.Equal(0, corner!.Value.X, 6); Assert.Equal(0, corner.Value.Y, 6);
        Assert.Equal(new[] { 0 }, paths);
    }

    [Fact]
    public void Marker25FromTheFailedExport_IgnoresTheExtraPathInItsColour()
    {
        // 0.5.4 export: marker 25's lines plus a 14 x 3 pt rectangle in the same colour
        // (fill-only in the PDF; even if counted, it is not an L and must not break the marker).
        var (corner, paths, detail) = AutomaticRegistrationService.MarkerCorner(new[]
        {
            new[] { P(1024.08984, 742.40444), P(1029.84866, 742.40444) },
            new[] { P(1024.08984, 742.40444), P(1024.08984, 746.24365) },
            new[] { P(763.35925, 76.42141), P(749.1889, 76.42141), P(749.1889, 73.27245), P(763.35925, 73.27245) },
        });
        Assert.True(corner.HasValue, detail);
        Assert.Equal(1024.08984, corner!.Value.X, 4); Assert.Equal(742.40444, corner.Value.Y, 4);
        Assert.Equal(new[] { 0, 1 }, paths);
    }

    [Fact]
    public void TwoDifferentLs_InTheSameColour_AreReportedNotGuessed()
    {
        var (corner, _, detail) = AutomaticRegistrationService.MarkerCorner(new[]
        {
            new[] { P(0, 0), P(6, 0) }, new[] { P(0, 0), P(0, 4) },
            new[] { P(100, 100), P(106, 100) }, new[] { P(100, 100), P(100, 104) },
        });
        Assert.Null(corner);
        Assert.Contains("2 possible", detail);
    }

    [Fact]
    public void NoL_GivesAClearMessage()
    {
        var (corner, _, detail) = AutomaticRegistrationService.MarkerCorner(new[] { new[] { P(0, 0), P(6, 0) }, new[] { P(10, 10), P(10, 14) } });
        Assert.Null(corner);
        Assert.Contains("not found", detail);
    }
}
