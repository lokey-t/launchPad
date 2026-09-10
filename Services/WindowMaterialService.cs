using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using LaunchPad.Models;

namespace LaunchPad.Services;

/// <summary>Compositor blur samples the windows behind this HWND, not application media.</summary>
public sealed class WindowMaterialService : IDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window,WindowMaterialService> Instances=new();
    public static ThemeProfile ForWindow(Window window,LauncherConfig config) => window!=null && Instances.TryGetValue(window,out var service) ? service._profile.Copy() : ThemeService.Global(config);
    public static WindowMaterialService Attach(Window window,System.Windows.Controls.Border card,Func<ThemeProfile> profile)
    {
        var service=new WindowMaterialService(window) { Surface=card };
        void Refresh()
        {
            var current=profile();
            ThemeService.Apply(window.Resources,current,0);
            if(current.Material=="Frosted")
            {
                // Nested panels should not hide the native glass with opaque fills.
                window.Resources["PanelBgBrush"]=new SolidColorBrush(Color.FromArgb(8,255,255,255));
                window.Resources["SubPanelBgBrush"]=new SolidColorBrush(Color.FromArgb(12,255,255,255));
            }
            card.Background=new SolidColorBrush(ThemeService.GlassSurfaceColor(current));
            service.Apply(current);
        }
        service.RefreshSurface=Refresh;
        window.Loaded+=(_,_)=>Refresh();
        window.IsVisibleChanged+=(_,_)=> { if(window.IsVisible) Refresh(); };
        Refresh(); return service;
    }
    public Action RefreshSurface { get; private set; }
    private readonly Window _window;
    private ThemeProfile _profile = new();
    private DispatcherTimer _transition;
    private double _strength;
    private HwndSource _backdrop;
    private NativeGaussianBackdrop _gaussian;
    public event Action Failed;
    private Rect _bounds=Rect.Empty;
    private int _clipWidth=-1,_clipHeight=-1,_clipDiameter=-1;
    private System.Windows.CornerRadius? _originalCorners;
    private bool _shown;
    private byte _alpha=255;
    public FrameworkElement Surface { get; set; }
    public bool IsActive { get; private set; }
    public WindowMaterialService(Window window)
    {
        _window=window;
        Instances.Remove(window); Instances.Add(window,this);
        window.SourceInitialized+=Initialized;
        window.Closed+=Closed;
        CompositionTarget.Rendering+=Rendering;
        window.IsVisibleChanged+=VisibilityChanged;
    }
    private void Initialized(object sender,EventArgs e)=>Apply(_profile);
    private void Rendering(object sender,EventArgs e)=>SyncBackdrop();
    private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)=>SyncBackdrop();
    private void Closed(object sender,EventArgs e)=>Dispose();
    public bool Apply(ThemeProfile profile,double duration=0)
    {
        _transition?.Stop(); _transition=null;
        _profile=profile.Copy();
        if(Surface is System.Windows.Controls.Border card)
        {
            _originalCorners??=card.CornerRadius;
            // DWM uses an 8-DIP round corner. Match the foreground to avoid two arcs.
            card.CornerRadius=profile.Material=="Frosted"?new CornerRadius(8):_originalCorners.Value;
        }
        if(new WindowInteropHelper(_window).Handle==IntPtr.Zero) return false;
        bool requested=profile.Material == "Frosted";
        if(requested && _backdrop==null)
        {
            try { CreateBackdrop(); }
            catch(Exception error) when(error is COMException or EntryPointNotFoundException or DllNotFoundException)
            {
                ReleaseBackdrop(); return false;
            }
        }
        if(_backdrop==null) return !requested;
        bool glass=requested;
        double start=_strength, target=glass?ThemeService.GlassStrength(profile):0;
        if(glass) IsActive=true;
        if(duration>0 && (IsActive || glass))
        {
            var started=System.Diagnostics.Stopwatch.StartNew();
            _transition=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(16) };
            _transition.Tick+=(_,_)=>
            {
                double t=Math.Min(1,started.Elapsed.TotalMilliseconds/duration);
                _strength=start+(target-start)*t;
                if(t>=1)
                {
                    _transition?.Stop(); _transition=null;
                    if(!glass) IsActive=false;
                }
                SyncBackdrop();
            };
            _transition.Start();
        }
        else
        {
            _strength=target;
            if(!glass) IsActive=false;
            SyncBackdrop();
        }
        return true;
    }
    private void CreateBackdrop()
    {
        // Keep the composition target inside the visible card, excluding its shadow gutter.
        var parameters=new HwndSourceParameters("LaunchPad material")
        {
            WindowStyle=unchecked((int)0x80000000),
            ExtendedWindowStyle=0x08000000|0x80|0x20,
            Width=1,Height=1
        };
        _backdrop=new HwndSource(parameters);
        _backdrop.CompositionTarget.BackgroundColor=Colors.Transparent;
        _backdrop.RootVisual=new DrawingVisual();
        // Use the same system corner as the foreground surface.
        int corners=2; DwmSetWindowAttribute(_backdrop.Handle,33,ref corners,sizeof(int));
        _backdrop.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>
        {
            if(message==0x84) { handled=true; return new IntPtr(-1); }
            if(message==0x21) { handled=true; return new IntPtr(3); }
            return IntPtr.Zero;
        });
        var margins=new Margins { Left=-1,Right=-1,Top=-1,Bottom=-1 };
        DwmExtendFrameIntoClientArea(_backdrop.Handle,ref margins);
        _gaussian=new NativeGaussianBackdrop(_backdrop.Handle);
    }
    private void SyncBackdrop()
    {
        if(_backdrop==null) return;
        var handle=_backdrop.Handle;
        if(!IsActive || _strength<=.001 || !_window.IsVisible || _window.WindowState==WindowState.Minimized || _window.Opacity<=.001)
        {
            if(_shown) { ShowWindow(handle,0); _shown=false; }
            return;
        }
        var surface=Surface??_window.Content as FrameworkElement;
        if(surface==null || !surface.IsVisible || surface.ActualWidth<=0 || surface.ActualHeight<=0) return;
        var start=surface.PointToScreen(new Point());
        var end=surface.PointToScreen(new Point(surface.ActualWidth,surface.ActualHeight));
        var bounds=new Rect(Math.Round(start.X),Math.Round(start.Y),Math.Max(1,Math.Round(end.X-start.X)),Math.Max(1,Math.Round(end.Y-start.Y)));
        var windowHandle=new WindowInteropHelper(_window).Handle;
        double radius=surface is System.Windows.Controls.Border border?border.CornerRadius.TopLeft:16;
        double scale=bounds.Width/surface.ActualWidth;
        int clipWidth=(int)bounds.Width,clipHeight=(int)bounds.Height;
        int clipDiameter=Math.Max(1,(int)Math.Round(radius*2*scale));
        bool clipChanged=clipWidth!=_clipWidth || clipHeight!=_clipHeight || clipDiameter!=_clipDiameter;
        if(bounds!=_bounds || clipChanged || !_shown || GetWindow(handle,3)!=windowHandle)
        {
            SetWindowPos(handle,windowHandle,(int)bounds.X,(int)bounds.Y,(int)bounds.Width,(int)bounds.Height,0x10|0x40);
            if(clipChanged || !_shown)
            {
                // GDI region right/bottom coordinates are exclusive. Extending them
                // by one pixel leaks square pixels outside the foreground corner.
                var region=CreateRoundRectRgn(0,0,clipWidth,clipHeight,clipDiameter,clipDiameter);
                if(region!=IntPtr.Zero && SetWindowRgn(handle,region,true)==0) DeleteObject(region);
                _clipWidth=clipWidth; _clipHeight=clipHeight; _clipDiameter=clipDiameter;
            }
            _bounds=bounds; _shown=true;
        }
        try { _gaussian.Update(clipWidth,clipHeight,(float)(60*_strength*_strength*scale),(float)_window.Opacity); }
        catch(COMException) { ReleaseBackdrop(); Failed?.Invoke(); return; }
        _alpha=(byte)Math.Clamp(_window.Opacity*255,0,255);
    }
    private void ReleaseBackdrop()
    {
        _transition?.Stop(); _transition=null;
        _gaussian?.Dispose(); _gaussian=null;
        _backdrop?.Dispose(); _backdrop=null;
        IsActive=false; _shown=false; _bounds=Rect.Empty;
        _clipWidth=_clipHeight=_clipDiameter=-1;
    }
    public void Dispose()
    {
        _transition?.Stop(); _transition=null;
        _window.SourceInitialized-=Initialized;
        _window.Closed-=Closed;
        CompositionTarget.Rendering-=Rendering;
        _window.IsVisibleChanged-=VisibilityChanged;
        ReleaseBackdrop();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left,Right,Top,Bottom; }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
}
