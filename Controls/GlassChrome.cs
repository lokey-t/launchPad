using System.Windows;
using System.Windows.Media;
using LaunchPad.Models;

namespace LaunchPad.Controls;

/// <summary>Surface reflections only; native background blur is separate from UI text.</summary>
public sealed class GlassChrome : FrameworkElement
{
    private ThemeProfile _profile;
    public double Radius { get; set; } = 16;
    public GlassChrome() { IsHitTestVisible = false; }
    public void SetProfile(ThemeProfile profile)
    {
        _profile = profile.Copy(); InvalidateVisual();
    }
    private static Color White(byte alpha) => Color.FromArgb(alpha,255,255,255);
    private static LinearGradientBrush Shine(params (double offset, Color color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0,0), EndPoint = new Point(1,1) };
        foreach(var (offset,color) in stops) brush.GradientStops.Add(new GradientStop(color,offset));
        brush.Freeze(); return brush;
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (_profile?.Material is not ("Frosted" or "Liquid") || ActualWidth<8 || ActualHeight<8) return;
        bool liquid = _profile.Material == "Liquid";
        var bounds = new Rect(1,1,ActualWidth-2,ActualHeight-2);
        var outer = new RectangleGeometry(bounds,Radius,Radius);
        dc.PushClip(outer);
        var wash=Shine((0,White(liquid?(byte)64:(byte)48)),(.44,White(8)),(1,Color.FromArgb(liquid?(byte)12:(byte)4,50,85,125)));
        dc.DrawRoundedRectangle(wash,null,bounds,Radius,Radius);
        if(!liquid)
        {
            dc.PushOpacity(.24);
            dc.DrawRectangle(Grain,null,bounds);
            dc.Pop();
        }
        var rimBrush=Shine((0,White(235)),(.25,White(70)),(.53,Color.FromArgb(28,50,95,140)),(.78,White(100)),(1,White(215)));
        dc.DrawRoundedRectangle(null,new Pen(rimBrush,liquid?1.7:1),bounds,Radius,Radius);
        if(liquid)
        {
            var inner=bounds; inner.Inflate(-3,-3);
            dc.DrawRoundedRectangle(null,new Pen(Shine((0,White(145)),(.34,White(10)),(.72,White(0)),(1,White(120))),1.2),inner,Math.Max(0,Radius-3),Math.Max(0,Radius-3));
            var highlight=new RadialGradientBrush(White(115),White(0)) { GradientOrigin=new Point(.25,0), Center=new Point(.25,0),RadiusX=.8,RadiusY=.9 };
            dc.DrawEllipse(highlight,null,new Point(ActualWidth*.25,0),ActualWidth*.65,Math.Min(100,ActualHeight*.23));
        }
        dc.Pop();
    }

    private static readonly DrawingBrush Grain = CreateGrain();
    private static DrawingBrush CreateGrain()
    {
        var group=new DrawingGroup(); var random=new Random(31);
        using(var dc=group.Open())
            for(int i=0;i<90;i++) dc.DrawEllipse(new SolidColorBrush(i%2==0?White(60):Color.FromArgb(20,0,0,0)),null,new Point(random.NextDouble()*48,random.NextDouble()*48),.38,.38);
        var brush=new DrawingBrush(group) { TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,48,48),ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,48,48),Stretch=Stretch.Fill };
        brush.Freeze(); return brush;
    }
}
