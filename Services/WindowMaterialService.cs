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
    private readonly Window _window;
    private ThemeProfile _profile = new();
    private DispatcherTimer _transition;
    private uint _tint;
    private HwndSource _backdrop;
    private Rect _bounds=Rect.Empty;
    private int _clipWidth=-1,_clipHeight=-1,_clipDiameter=-1;
    private bool _shown;
    private byte _alpha=255;
    public FrameworkElement Surface { get; set; }
    public bool IsActive { get; private set; }
    public WindowMaterialService(Window window)
    {
        _window=window;
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
        if(new WindowInteropHelper(_window).Handle==IntPtr.Zero) return false;
        bool requested=profile.Material is "Frosted" or "Liquid";
        if(requested && _backdrop==null) CreateBackdrop();
        if(_backdrop==null) return !requested;
        var handle=_backdrop.Handle;
        bool glass=profile.Material is "Frosted" or "Liquid";
        var color=ThemeService.Parse(profile.Surface,"#FFFFFF");
        var policy=new AccentPolicy
        {
            State=glass?4:0,
            Flags=2,
            GradientColor=(uint)((profile.Material=="Frosted"?100:24)<<24 | color.B<<16 | color.G<<8 | color.R)
        };
        var start=_tint;
        var target=policy.GradientColor;
        if(duration>0 && IsActive)
        {
            var started=System.Diagnostics.Stopwatch.StartNew();
            _transition=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(25) };
            _transition.Tick+=(_,_)=>
            {
                double t=Math.Min(1,started.Elapsed.TotalMilliseconds/duration);
                uint blended=0;
                for(int shift=0;shift<32;shift+=8)
                {
                    var a=(start>>shift)&255; var b=(target>>shift)&255;
                    blended|=(uint)(a+(b-(double)a)*t)<<shift;
                }
                if(glass) { policy.GradientColor=blended; SetPolicy(handle,policy); }
                if(t>=1)
                {
                    _transition?.Stop(); _transition=null;
                    if(!glass) { SetPolicy(handle,policy); SyncBackdrop(); }
                }
            };
            _transition.Start();
            return true;
        }
        bool applied=SetPolicy(handle,policy);
        SyncBackdrop();
        return applied;
    }
    private bool SetPolicy(IntPtr handle,AccentPolicy policy)
    {
        bool glass=policy.State!=0;
        var memory=Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy,memory,false);
            var data=new CompositionData { Attribute=19,Data=memory,Size=Marshal.SizeOf<AccentPolicy>() };
            bool success=SetWindowCompositionAttribute(handle,ref data)!=0;
            IsActive=glass&&success;
            _tint=policy.GradientColor;
            return !glass||success;
        }
        catch(EntryPointNotFoundException) { IsActive=false; return !glass; }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private void CreateBackdrop()
    {
        // A separate, non-layered composition surface confines native acrylic to the
        // visible card. Applying acrylic to the WPF HWND also paints its shadow gutter.
        var parameters=new HwndSourceParameters("LaunchPad material")
        {
            WindowStyle=unchecked((int)0x80000000),
            ExtendedWindowStyle=0x80|0x20,
            Width=1,Height=1
        };
        _backdrop=new HwndSource(parameters);
        _backdrop.CompositionTarget.BackgroundColor=Colors.Transparent;
        _backdrop.RootVisual=new DrawingVisual();
        // DWMWCP_ROUND (2).  A value of 1 means DWMWCP_DONOTROUND and made
        // the native glass surface visibly square beneath the WPF card.
        int corners=2; DwmSetWindowAttribute(_backdrop.Handle,33,ref corners,sizeof(int));
        _backdrop.AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>
        {
            if(message==0x84) { handled=true; return new IntPtr(-1); }
            if(message==0x21) { handled=true; return new IntPtr(3); }
            return IntPtr.Zero;
        });
        var margins=new Margins { Left=-1,Right=-1,Top=-1,Bottom=-1 };
        DwmExtendFrameIntoClientArea(_backdrop.Handle,ref margins);
    }
    private void SyncBackdrop()
    {
        if(_backdrop==null) return;
        var handle=_backdrop.Handle;
        if(!IsActive || !_window.IsVisible || _window.WindowState==WindowState.Minimized || _window.Opacity<=.001)
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
        byte alpha=(byte)Math.Clamp(_window.Opacity*255,0,255);
        if(alpha!=_alpha)
        {
            // Constant-alpha fading preserves the native surface; per-pixel WPF
            // transparency is kept on the foreground window only.
            SetWindowLongPtr(handle,-20,new IntPtr(0x80|0x20|0x80000));
            SetLayeredWindowAttributes(handle,0,alpha,2); _alpha=alpha;
        }
    }
    public void Dispose()
    {
        _transition?.Stop(); _transition=null;
        _window.SourceInitialized-=Initialized;
        _window.Closed-=Closed;
        CompositionTarget.Rendering-=Rendering;
        _window.IsVisibleChanged-=VisibilityChanged;
        _backdrop?.Dispose(); _backdrop=null;
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
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint color,byte alpha,uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int State,Flags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
}
