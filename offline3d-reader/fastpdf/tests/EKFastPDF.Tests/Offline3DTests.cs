using EKFastPDF.Core.Rendering;
using Offline3D;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
namespace EKFastPDF.Tests;
[Collection("pool")]
public sealed class Offline3DTests
{
    private readonly PoolFixture fixture;
    public Offline3DTests(PoolFixture fixture){this.fixture=fixture;}
    [Theory]
    [InlineData(0,10,760,590,20)]
    [InlineData(90,10,20,590,760)]
    [InlineData(180,590,20,10,760)]
    [InlineData(270,590,760,10,20)]
    public async Task NativeMapping_UsesEffectiveCropRotationAndUserUnit(int rotation,double x0,double y0,double x1,double y1)
    {
        string file=Path.Combine(fixture.SampleDir,"offline3d-map-"+rotation+".pdf");
        using(var pdf=new PdfDocument()){var page=pdf.AddPage();page.Width=XUnit.FromPoint(612);page.Height=XUnit.FromPoint(792);page.Rotate=rotation;page.CropBox=new PdfRectangle(new XPoint(10,20),new XPoint(590,760));page.Elements.SetReal("/UserUnit",2);pdf.Save(file);}
        int id=fixture.Pool.RegisterDocument(file);
        try{await fixture.Pool.OpenDocumentAsync(id);var map=await fixture.Pool.GetPageMapAsync(id,0);var a=map.Apply(0,0);var b=map.Apply(1,1);
            Assert.Equal(x0,a.X,3);Assert.Equal(y0,a.Y,3);Assert.Equal(x1,b.X,3);Assert.Equal(y1,b.Y,3);
            foreach(var point in new[]{(.12,.23),(.5,.5),(.91,.84)}){var raw=map.Apply(point.Item1,point.Item2);var displayed=map.Unapply(raw.X,raw.Y);Assert.Equal(point.Item1,displayed.U,10);Assert.Equal(point.Item2,displayed.V,10);}
        }finally{fixture.Pool.CloseDocument(id);}
    }
    [Fact]
    public async Task NativePageClick_ResolvesPackagedModelCoordinates()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"SYNTHETIC-Offline3D.pdf");
        var package=new PdfPackageService().Read(path);int id=fixture.Pool.RegisterDocument(path);
        try{var info=await fixture.Pool.OpenDocumentAsync(id);var map=await fixture.Pool.GetPageMapAsync(id,0);var raw=map.Apply(285d/info.PageWidths[0],442d/info.PageHeights[0]);var p=Coordinates.PlanClick(package.Registration,raw.X,raw.Y);
            Assert.InRange(Math.Abs(p.X-15)*.3048,0,.001);Assert.InRange(Math.Abs(p.Y-10)*.3048,0,.001);Assert.Equal(0,p.Z,8);
        }finally{fixture.Pool.CloseDocument(id);}
    }
    [Fact]
    public void FootprintMapping_RejectsSingularPageMap()=>Assert.Throws<InvalidDataException>(()=>new PdfPageMap(0,0,1,2,2,4).Unapply(1,2));
}
