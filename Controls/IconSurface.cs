using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LaunchPad.Models;
using LaunchPad.Services;
namespace LaunchPad.Controls;
public sealed class IconSurface : Border
{
    public static readonly DependencyProperty ProfileProperty=DependencyProperty.Register(nameof(Profile),typeof(ThemeProfile),typeof(IconSurface),new PropertyMetadata(null,(d,e)=>((IconSurface)d).Refresh()));
    public ThemeProfile Profile { get=>(ThemeProfile)GetValue(ProfileProperty); set=>SetValue(ProfileProperty,value); }
    private AppItem _entry;
    public IconSurface()
    {
        SetResourceReference(ProfileProperty,"IconAppearanceProfile");
        Loaded+=(_,_)=>Connect(); Unloaded+=(_,_)=>Disconnect(); DataContextChanged+=(_,_)=> { if(IsLoaded) Connect(); };
    }
    private void Disconnect() { if(_entry!=null) PropertyChangedEventManager.RemoveHandler(_entry,Changed,""); _entry=null; }
    private void Connect() { Disconnect(); _entry=DataContext as AppItem; if(_entry!=null) PropertyChangedEventManager.AddHandler(_entry,Changed,""); Refresh(); }
    private void Changed(object sender,PropertyChangedEventArgs e) { if(e.PropertyName is "IconBackground" or "IconOpacity") Refresh(); }
    private void Refresh()
    {
        var p=Profile??new ThemeProfile(); var color=ThemeService.Parse(_entry?.IconBackground??p.IconBackground,"#FFFFFF");
        double alpha=_entry?.IconOpacity??p.IconOpacity; color.A=(byte)Math.Round(255*(double.IsFinite(alpha)?Math.Clamp(alpha,0,1):0));
        Background=new SolidColorBrush(color);
        BorderBrush=new SolidColorBrush(ThemeService.Parse(p.IconBorderColor,"#FFFFFF"));
        BorderThickness=new Thickness(p.IconBorderMode=="Line" && double.IsFinite(p.IconBorderWidth)?Math.Clamp(p.IconBorderWidth,0,8):0);
        Effect=p.IconBorderMode=="Shadow"?new DropShadowEffect { Color=Colors.Black, Opacity=double.IsFinite(p.IconShadowStrength)?Math.Clamp(p.IconShadowStrength,0,1):.3, BlurRadius=10, ShadowDepth=double.IsFinite(p.IconShadowDepth)?Math.Clamp(p.IconShadowDepth,0,12):3, Direction=double.IsFinite(p.IconShadowDirection)?p.IconShadowDirection:270 }:null;
    }
}
