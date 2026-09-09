using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace LaunchPad;
public partial class SettingsWindow
{
    private Slider _hoverDragSlider;
    private double _hoverRawValue;
    private bool _adjustingHover;
    private static double SnapHoverValue(double value)
    {
        foreach(double stop in new[] { .5,1d,2d })
            if(Math.Abs(value-stop)<=.045) return stop;
        return value;
    }
    private void InitializeHoverMagnets()
    {
        foreach(var slider in new[] { FolderHoverDelay,CategoryHoverDelay })
        {
            slider.IsMoveToPointEnabled=true; slider.SmallChange=.05; slider.LargeChange=.5;
            slider.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler((_,_)=> { _hoverDragSlider=slider; _hoverRawValue=slider.Value; }),true);
            slider.AddHandler(Thumb.DragDeltaEvent,new DragDeltaEventHandler((_,e)=>
            {
                if(_hoverDragSlider!=slider || slider.Template.FindName("PART_Track",slider) is not Track track) return;
                double length=track.ActualWidth-(track.Thumb?.ActualWidth??16);
                if(length<=0) return;
                // Accumulate unsnapped movement so slow dragging can always leave a magnetic stop.
                _hoverRawValue=Math.Clamp(_hoverRawValue+e.HorizontalChange/length*(slider.Maximum-slider.Minimum),slider.Minimum,slider.Maximum);
                slider.SetCurrentValue(Slider.ValueProperty,SnapHoverValue(_hoverRawValue));
                e.Handled=true;
            }),true);
            slider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>_hoverDragSlider=null),true);
        }
    }
}
