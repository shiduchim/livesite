using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EKRibbon
{
    // Every image the add-in shows. Vector icons are drawn here so nothing depends on
    // image files; command icons are loaded from the PNGs the scanner captured.
    internal static class Icons
    {
        static readonly Color Grey=Color.FromRgb(0x7D,0x88,0x94);

        static ImageSource Render(int size,double design,Action<DrawingContext> draw)
        {
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size/design,size/design));
                draw(dc);
                dc.Pop();
            }
            var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        static SolidColorBrush Solid(Color c,double opacity=1)
        {
            var b=new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity*255),c.R,c.G,c.B));
            b.Freeze();
            return b;
        }
        static Brush Vertical(Color top,Color bottom)
        {
            var b=new LinearGradientBrush(top,bottom,90);
            b.Freeze();
            return b;
        }
        static Pen Stroke(Color c,double width,double opacity=1)
        {
            var p=new Pen(Solid(c,opacity),width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            p.Freeze();
            return p;
        }
        static void Box(DrawingContext dc,Brush fill,Pen pen,double x,double y,double w,double h,double r)
        {
            dc.DrawRoundedRectangle(fill,pen,new Rect(x,y,w,h),r,r);
        }

        // Ribbon Builder: a ribbon window with the active tab in blue, a large button,
        // two stacked buttons, a divider and a document. Drawn for exactly 16 and 32 px.
        static readonly Color Frame=Color.FromRgb(0x4A,0x55,0x63), TabBar=Color.FromRgb(0xCD,0xD3,0xDA),
            TabSlot=Color.FromRgb(0x7A,0x85,0x92), Divider=Color.FromRgb(0xB9,0xC0,0xC8),
            Brand=Color.FromRgb(0x1E,0x7B,0xE6), BrandTop=Color.FromRgb(0x2A,0x8C,0xF0), BrandEdge=Color.FromRgb(0x0D,0x5B,0xBF);
        internal static ImageSource App(int size)
        {
            if(size<24)return Render(size,16,dc=>{
                Box(dc,Vertical(Color.FromRgb(0xF7,0xF8,0xFA),Color.FromRgb(0xE6,0xEA,0xEE)),Stroke(Frame,1),.75,2.25,14.5,11.5,2);
                dc.DrawGeometry(Solid(TabBar),null,Geometry.Parse("M1.25,5.25 V3.9 A1.4,1.4 0 0 1 2.65,2.75 H13.35 A1.4,1.4 0 0 1 14.75,3.9 V5.25 Z"));
                dc.DrawGeometry(Solid(Brand),null,Geometry.Parse("M1.25,5.25 V3.9 A1.4,1.4 0 0 1 2.65,2.75 H6 V5.25 Z"));
                Box(dc,Solid(TabSlot),null,7,3.5,3,1,.5);
                Box(dc,Solid(TabSlot),null,11,3.5,3,1,.5);
                Box(dc,Solid(Brand),null,2.5,6.5,3.5,5.8,.4);
                Box(dc,Solid(Brand),null,7,6.5,2.4,2.4,.4);
                Box(dc,Brushes.White,Stroke(Frame,.7),7,9.9,2.4,2.4,.4);
                Box(dc,Solid(Divider),null,10.4,6.4,.7,6,0);
                Box(dc,Brushes.White,Stroke(TabSlot,.6),11.9,6.5,2.2,5.8,.2);
                Box(dc,Solid(Brand),null,12.4,8.3,1.2,.8,0);
                Box(dc,Solid(Brand),null,12.4,10,1.2,.8,0);
            });
            return Render(size,32,dc=>{
                var blue=Vertical(BrandTop,Brand);
                Box(dc,Vertical(Color.FromRgb(0xF7,0xF8,0xFA),Color.FromRgb(0xE6,0xEA,0xEE)),Stroke(Frame,1.5),1.25,4.25,29.5,23.5,3.5);
                dc.DrawGeometry(Solid(TabBar),null,Geometry.Parse("M2,10.5 V7.5 A2.75,2.75 0 0 1 4.75,5 H27.25 A2.75,2.75 0 0 1 30,7.5 V10.5 Z"));
                dc.DrawGeometry(blue,null,Geometry.Parse("M2,10.5 V7.5 A2.75,2.75 0 0 1 4.75,5 H12 V10.5 Z"));
                Box(dc,Solid(TabSlot),null,14,6.7,6.5,2.1,1);
                Box(dc,Solid(TabSlot),null,22,6.7,6.5,2.1,1);
                // Large button, drawn as a block with a light side face.
                dc.DrawGeometry(Solid(Color.FromRgb(0xD5,0xDA,0xDF)),Stroke(BrandEdge,.8),Geometry.Parse("M4,14 L6,12.8 V24.2 L4,23 Z"));
                Box(dc,blue,Stroke(BrandEdge,.8),6,12.8,5.5,11.4,.6);
                Box(dc,blue,Stroke(Frame,.9),13.4,13,4.2,4.2,.8);
                Box(dc,Brushes.White,Stroke(Frame,.9),13.4,19.6,4.2,4.2,.8);
                Box(dc,Solid(Brand),null,13.85,21.9,3.3,1.45,0);
                Box(dc,Solid(Divider),null,19.3,12.6,1,11.8,.5);
                dc.DrawGeometry(Brushes.White,Stroke(Frame,1),Geometry.Parse("M22,13 H26.2 L28.5,15.4 V24 H22 Z"));
                dc.DrawGeometry(Solid(Brand),null,Geometry.Parse("M26.2,13 L28.5,15.4 H26.2 Z"));
                Box(dc,Solid(Brand),null,23.2,18,4,1.3,.65);
                Box(dc,Solid(Brand),null,23.2,20.6,4,1.3,.65);
            });
        }

        // Push pin: upright and blue when the tab is pinned, tilted grey outline when not.
        const string PinShape="M8,2.5 H16 A1.25,1.25 0 0 1 16,5 H15 V9.2 C15,11 16.4,12.4 18.2,12.6 V14.6 H13 V20.6 L12,22.4 L11,20.6 V14.6 H5.8 V12.6 C7.6,12.4 9,11 9,9.2 V5 H8 A1.25,1.25 0 0 1 8,2.5 Z";
        internal static ImageSource Pin(bool pinned,int size)
        {
            return Render(size,22,dc=>{
                dc.PushTransform(new TranslateTransform(-1,-1.5));
                var shape=Geometry.Parse(PinShape);
                if(pinned)
                {
                    var body=new LinearGradientBrush(Color.FromRgb(0x3A,0x93,0xE6),Color.FromRgb(0x1A,0x5F,0xA8),0);
                    body.Freeze();
                    dc.DrawGeometry(body,Stroke(Color.FromRgb(0x0F,0x4C,0x8C),size<24?.7:.9),shape);
                    if(size>=24)Box(dc,Solid(Colors.White,.45),null,10,5.4,1.4,5,.7);
                }
                else
                {
                    dc.PushTransform(new RotateTransform(40,12,12));
                    dc.DrawGeometry(Solid(Colors.White,.9),Stroke(Grey,size<24?1.3:1.5),shape);
                    dc.Pop();
                }
                dc.Pop();
            });
        }

        // Cog for "Settings" commands that expose no bitmap of their own.
        internal static ImageSource Gear(int size,int zoom=100)
        {
            size=Clamp(size,16,64);
            double z=Clamp(zoom,50,175)/100.0, c=size/2.0, outer=size*.465*z, inner=size*.355*z, core=size*.155*z;
            var figure=new PathFigure{IsClosed=true,IsFilled=true};
            double[] offsets={-.47,-.28,.28,.47};
            for(int i=0;i<10;i++)
                for(int j=0;j<4;j++)
                {
                    double a=2*Math.PI*(i+offsets[j])/10-Math.PI/2, r=j==1||j==2?outer:inner;
                    var pt=new Point(c+Math.Cos(a)*r,c+Math.Sin(a)*r);
                    if(i==0&&j==0)figure.StartPoint=pt;else figure.Segments.Add(new LineSegment(pt,true));
                }
            var shape=Geometry.Combine(new PathGeometry(new[]{figure}),new EllipseGeometry(new Point(c,c),core,core),
                GeometryCombineMode.Exclude,null);
            return Render(size,size,dc=>dc.DrawGeometry(Solid(Color.FromRgb(26,106,179)),null,shape));
        }

        // Clipboard, used when Revit exposes no icon for the Paste Aligned commands.
        internal static ImageSource Paste(int size,int zoom=100)
        {
            size=Clamp(size,16,64);
            double s=size*Clamp(zoom,75,145)/100.0, o=(size-s)/2.0;
            return Render(size,size,dc=>{
                dc.PushTransform(new TranslateTransform(o,o));
                var dark=Solid(Color.FromRgb(34,79,118));
                var line=new Pen(dark,Math.Max(.7,s*.045));
                Box(dc,Solid(Color.FromRgb(44,115,174)),line,s*.17,s*.17,s*.68,s*.77,s*.07);
                Box(dc,Brushes.White,line,s*.28,s*.32,s*.46,s*.49,s*.02);
                Box(dc,Solid(Color.FromRgb(191,215,232)),line,s*.35,s*.10,s*.32,s*.16,s*.035);
                var rule=new Pen(Solid(Color.FromRgb(44,115,174)),Math.Max(.7,s*.045));
                for(int i=0;i<3;i++)dc.DrawLine(rule,new Point(s*.37,s*(.47+i*.11)),new Point(s*.64,s*(.47+i*.11)));
                dc.Pop();
            });
        }

        // Fallback for commands without an icon: their initials on a calm colour tile.
        static readonly Color[] Palette={Color.FromRgb(0x2F,0x7D,0xC9),Color.FromRgb(0x2E,0x9E,0x8F),
            Color.FromRgb(0x7A,0x5C,0xC4),Color.FromRgb(0xD0,0x7A,0x2C),Color.FromRgb(0xC2,0x4F,0x6B),
            Color.FromRgb(0x4C,0x8C,0x3A),Color.FromRgb(0x5B,0x6B,0x7F),Color.FromRgb(0x1F,0x8A,0xB8)};
        internal static ImageSource Initials(string name,int size,int zoom=100)
        {
            size=Clamp(size,16,64);
            var words=(name??"").Split(new[]{' ','-','_','/','\n','\r','\t'},StringSplitOptions.RemoveEmptyEntries);
            string text="";
            foreach(var w in words){foreach(char ch in w)if(char.IsLetterOrDigit(ch)){text+=char.ToUpperInvariant(ch);break;}if(text.Length==2)break;}
            if(text.Length==0)text="EK";
            if(size<24)text=text.Substring(0,1);
            int hash=0;foreach(char ch in name??"")hash=unchecked(hash*31+ch);
            var color=Palette[(hash&0x7fffffff)%Palette.Length];
            double s=size*Clamp(zoom,50,175)/100.0, o=(size-s)/2.0;
            return Render(size,size,dc=>{
                var top=Color.FromRgb((byte)Math.Min(255,color.R+28),(byte)Math.Min(255,color.G+28),(byte)Math.Min(255,color.B+28));
                Box(dc,Vertical(top,color),null,o+s*.03,o+s*.03,s*.94,s*.94,s*.22);
                var ft=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),
                    s*(text.Length>1?.40:.56),Brushes.White,1.0);
                dc.DrawText(ft,new Point(o+(s-ft.Width)/2,o+(s-ft.Height)/2));
            });
        }

        static readonly Dictionary<int,ImageSource> clear=new Dictionary<int,ImageSource>();
        internal static ImageSource Transparent(int size)
        {
            if(clear.TryGetValue(size,out var cached))return cached;
            var bmp=BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,new byte[size*size*4],size*4);
            bmp.Freeze();
            return clear[size]=bmp;
        }

        internal static bool IsSettings(CommandInfo ci)
        {
            string name=(ci?.Name??"").Trim();
            return name.Equals("Settings",StringComparison.OrdinalIgnoreCase)||name.Equals("EK Settings",StringComparison.OrdinalIgnoreCase)
                ||name.Equals("Ribbon Settings",StringComparison.OrdinalIgnoreCase)||name.Equals("Launcher Settings",StringComparison.OrdinalIgnoreCase);
        }

        // The command's own icon, or the best stand-in. Never null.
        internal static ImageSource Command(CommandInfo ci,int pixels,int zoom=100)
        {
            if(IsSettings(ci))return Gear(pixels,zoom);
            var result=File(ci?.Icon,pixels,zoom);
            if(result!=null)return result;
            if((ci?.Id??"").StartsWith("POSTABLE:AlignedTo",StringComparison.OrdinalIgnoreCase))return Paste(pixels,zoom);
            return Initials(ci?.Name,pixels,zoom);
        }

        // Revit wants exactly 16 x 16 / 32 x 32 images: a larger bitmap is clipped, not scaled.
        // Render at the exact size and 96 DPI, centred, keeping aspect ratio and transparency.
        static readonly Dictionary<string,ImageSource> cache=new Dictionary<string,ImageSource>();
        internal static ImageSource File(string path,int pixels,int zoom=100)
        {
            try
            {
                if(string.IsNullOrWhiteSpace(path)||!System.IO.File.Exists(path))return null;
                pixels=Clamp(pixels,16,64);
                zoom=Clamp(zoom,50,175);
                string key=path+"|"+pixels+"|"+zoom+"|"+System.IO.File.GetLastWriteTimeUtc(path).Ticks;
                if(cache.TryGetValue(key,out var hit))return hit;
                if(cache.Count>900)cache.Clear();
                var source=new BitmapImage();
                source.BeginInit();
                source.UriSource=new Uri(Path.GetFullPath(path));
                source.CacheOption=BitmapCacheOption.OnLoad;
                source.CreateOptions=BitmapCreateOptions.IgnoreImageCache;
                source.EndInit();
                source.Freeze();
                return cache[key]=Fit(source,source.PixelWidth,source.PixelHeight,pixels,zoom);
            }
            catch(Exception ex){Log.ErrorOnce("Decode icon "+ManifestPaths.Printable(path),ex);return null;}
        }

        // Re-renders a live ImageSource (another add-in's toggle icon) at ribbon size.
        internal static ImageSource Zoomed(ImageSource source,int pixels,int zoom)
        {
            if(source==null||zoom==100)return source;
            double w=source.Width,h=source.Height;
            if(w<=0||h<=0||double.IsNaN(w)||double.IsNaN(h))return source;
            return Fit(source,w,h,Clamp(pixels,16,64),Clamp(zoom,50,175));
        }

        static ImageSource Fit(ImageSource source,double w,double h,int pixels,int zoom)
        {
            double ratio=h>0?w/h:1;
            if(double.IsNaN(ratio)||ratio<=0)ratio=1;
            double box=pixels*zoom/100.0, width=ratio>=1?box:box*ratio, height=ratio<=1?box:box/ratio;
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())
            {
                dc.PushClip(new RectangleGeometry(new Rect(0,0,pixels,pixels)));
                dc.DrawImage(source,new Rect((pixels-width)/2,(pixels-height)/2,width,height));
                dc.Pop();
            }
            var bitmap=new RenderTargetBitmap(pixels,pixels,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        static int Clamp(int v,int lo,int hi) {return Math.Max(lo,Math.Min(hi,v));}
    }
}
