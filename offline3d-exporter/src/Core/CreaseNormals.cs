namespace Offline3D;
internal static class CreaseNormals
{
    public static Point3[] Compute(IReadOnlyList<Point3> points,IReadOnlyList<Point3> supplied){
        var groups=new Dictionary<string,List<int>>();var normals=new Point3[points.Count];
        for(int i=0;i<points.Count;i+=3){var a=points[i+1]-points[i];var b=points[i+2]-points[i];var face=new Point3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);face=face*(1/Math.Max(face.Length,1e-20));for(int j=0;j<3;j++){int index=i+j;var n=supplied.Count==points.Count?supplied[index]:face;normals[index]=n*(1/Math.Max(n.Length,1e-20));var p=points[index]-points[0];string key=Math.Round(p.X,7).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+Math.Round(p.Y,7).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+Math.Round(p.Z,7).ToString("R",System.Globalization.CultureInfo.InvariantCulture);if(!groups.TryGetValue(key,out var group)){group=new List<int>();groups.Add(key,group);}group.Add(index);}}
        var result=(Point3[])normals.Clone();double cosine=Math.Cos(Math.PI/6);foreach(var group in groups.Values)foreach(int i in group){var n=normals[i];var sum=new Point3();foreach(int j in group){var other=normals[j];if(n.X*other.X+n.Y*other.Y+n.Z*other.Z>=cosine)sum+=other;}result[i]=sum*(1/Math.Max(sum.Length,1e-20));}return result;
    }
}
