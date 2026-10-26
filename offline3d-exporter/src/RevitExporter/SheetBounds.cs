namespace Offline3D.RevitExporter;

// Plain geometry, shared with the portable regression tests. Bounds include
// viewport bodies and view-title outlines; touching edges are not ambiguous.
internal readonly struct SheetBounds
{
    private readonly double left,bottom,right,top;
    public SheetBounds(double left,double bottom,double right,double top)
    {
        if(double.IsNaN(left)||double.IsNaN(bottom)||double.IsNaN(right)||double.IsNaN(top)||double.IsInfinity(left)||double.IsInfinity(bottom)||double.IsInfinity(right)||double.IsInfinity(top)||right<left||top<bottom)
            throw new System.IO.InvalidDataException("Invalid viewport bounds.");
        this.left=left;this.bottom=bottom;this.right=right;this.top=top;
    }
    public bool Overlaps(SheetBounds other)=>left<other.right&&right>other.left&&bottom<other.top&&top>other.bottom;
}
