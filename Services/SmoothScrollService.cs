using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
namespace LaunchPad.Services;

public static class SmoothScrollService
{
    private static bool _initialized;
    private static readonly ConditionalWeakTable<ScrollViewer, Motion> Motions=new();
    public static void Initialize()
    {
        if(_initialized) return;
        _initialized=true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,e)=>
        {
            if(e.OriginalSource==sender) ((ScrollViewer)sender).CanContentScroll=false;
        }));
        EventManager.RegisterClassHandler(typeof(ScrollViewer),UIElement.PreviewMouseDownEvent,new MouseButtonEventHandler((sender,_)=> { if(Motions.TryGetValue((ScrollViewer)sender,out var motion)) motion.Stop(); }));
        EventManager.RegisterClassHandler(typeof(ScrollViewer),UIElement.PreviewMouseWheelEvent,new MouseWheelEventHandler(Wheel));
    }
    private static void Wheel(object sender,MouseWheelEventArgs e)
    {
        if(e.Handled || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var scroll=(ScrollViewer)sender;
        // Route to the nearest scrollable viewer rather than stealing nested list scrolling.
        for(var node=e.OriginalSource as DependencyObject;node!=null;)
        {
            if(node is ScrollViewer candidate && (candidate.ScrollableHeight>0 || candidate.ScrollableWidth>0))
            {
                if(candidate!=scroll) return;
                break;
            }
            node=node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        bool horizontal=scroll.ScrollableHeight<=0 && scroll.ScrollableWidth>0;
        double max=horizontal?scroll.ScrollableWidth:scroll.ScrollableHeight;
        if(max<=0) return;
        var motion=Motions.GetValue(scroll,s=>new Motion(s));
        double current=horizontal?scroll.HorizontalOffset:scroll.VerticalOffset;
        double lines=SystemParameters.WheelScrollLines;
        if(lines==0) return;
        double distance=lines<0?(horizontal?scroll.ViewportWidth:scroll.ViewportHeight):Math.Max(1,lines)*16;
        double target=Math.Clamp((motion.Active && motion.Horizontal==horizontal?motion.Target:current)-e.Delta/120d*distance,0,max);
        if(Math.Abs(target-current)<.1 && !motion.Active) return; // Let a parent scroll at the edge.
        motion.Go(horizontal,current,target); e.Handled=true;
    }
    private sealed class Motion : Animatable
    {
        private readonly ScrollViewer _scroll;
        private bool _suppress;
        public bool Active { get; private set; }
        public bool Horizontal { get; private set; }
        public double Target { get; private set; }
        private static readonly DependencyProperty PositionProperty=DependencyProperty.Register("Position",typeof(double),typeof(Motion),new PropertyMetadata(0d,(d,e)=>
        {
            var motion=(Motion)d;
            if(motion._suppress) return;
            if(motion.Horizontal) motion._scroll.ScrollToHorizontalOffset((double)e.NewValue);
            else motion._scroll.ScrollToVerticalOffset((double)e.NewValue);
        }));
        public Motion(ScrollViewer scroll) { _scroll=scroll; }
        protected override Freezable CreateInstanceCore()=>new Motion(_scroll);
        public void Stop()
        {
            _suppress=true; BeginAnimation(PositionProperty,null); _suppress=false; Active=false;
        }
        public void Go(bool horizontal,double current,double target)
        {
            Stop(); Horizontal=horizontal; Target=target;
            _suppress=true; SetValue(PositionProperty,current); _suppress=false;
            double duration=MotionService.Duration((System.Windows.Application.Current as App)?.Config,150);
            if(duration<=0) { SetValue(PositionProperty,target); return; }
            Active=true;
            var animation=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(duration)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } };
            animation.Completed+=(_,_)=>Active=false;
            BeginAnimation(PositionProperty,animation);
        }
    }
}
