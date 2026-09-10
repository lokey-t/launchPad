using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LaunchPad.Models;
using LaunchPad.Services;

namespace LaunchPad.Controls;

/// <summary>Two live layers: keep the previous media visible until the next has loaded.</summary>
public sealed class ThemeBackground : Grid, IDisposable
{
    private int _revision;
    private string _key;
    private Layer _pending;
    private Task _currentRequest;
    private bool _disposed;
    public double Radius { get; set; }=16;
    public event Action<string> Failed;
    public ThemeBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(RenderSize), Radius, Radius);
    }

    public Task SetAsync(ThemeProfile profile, double duration) => Dispatcher.InvokeAsync(()=>
    {
        if (_disposed) return Task.CompletedTask;
        profile=profile.Copy();
        Radius=profile.Material=="Frosted"?8:16;
        Clip=new RectangleGeometry(new Rect(RenderSize),Radius,Radius);
        if(profile.Material == "Frosted") profile.BackgroundPath=null;
        string key=$"{profile.BackgroundPath}|{profile.BackgroundDim}|{profile.Surface}|{profile.Material}|{ThemeService.GlassDepth(profile)}";
        return key==_key && _currentRequest!=null ? _currentRequest : (_currentRequest=SetCoreAsync(profile.Copy(),duration));
    }).Task.Unwrap();

    private async Task SetCoreAsync(ThemeProfile profile, double duration)
    {
        var key = $"{profile.BackgroundPath}|{profile.BackgroundDim}|{profile.Surface}|{profile.Material}|{ThemeService.GlassDepth(profile)}";
        if (key == _key) return;
        if (_pending == null && Children.Cast<Layer>().LastOrDefault() is Layer current && current.Path == profile.BackgroundPath)
        {
            _key=key;
            current.UpdateAppearance(profile,duration);
            return;
        }
        _key = key;
        var revision = ++_revision;
        if (_pending != null) { Children.Remove(_pending); _pending.Dispose(); }
        var next = new Layer(profile);
        next.Opacity = 0;
        Children.Add(next);
        _pending = next;
        try { await next.LoadAsync(); }
        catch (Exception)
        {
            if (revision != _revision) { next.Dispose(); return; }
            Children.Remove(next); next.Dispose();
            var fallback=profile.Copy(); fallback.BackgroundPath=null;
            next = new Layer(fallback);
            Failed?.Invoke("背景无法加载，已使用主题底色。请重新选择素材或更换视频编码。");
            _key = null; // Allow retry after the file has been repaired.
        }
        if (revision != _revision) { next.Dispose(); return; }
        _pending = null;
        // Preserve the visible blend when rapid category changes interrupt a transition.
        var previous = Children.Cast<Layer>().Where(x=>x!=next).ToArray();
        if (previous.Length>1 && ActualWidth>0 && ActualHeight>0)
        {
            var snapshot=new RenderTargetBitmap(Math.Max(1,(int)ActualWidth),Math.Max(1,(int)ActualHeight),96,96,PixelFormats.Pbgra32);
            snapshot.Render(this); snapshot.Freeze();
            foreach(var old in previous) { Children.Remove(old); old.Dispose(); }
            var still=new Layer(new ThemeProfile { Surface=profile.Surface });
            still.Children.Add(new Image { Source=snapshot,Stretch=Stretch.Fill });
            Children.Insert(0,still);
        }
        next.Opacity = duration > 0 ? 0 : 1;
        if (!Children.Contains(next)) Children.Add(next);
        void Clean()
        {
            foreach (Layer old in Children.Cast<Layer>().Where(x => x != next).ToArray())
            { Children.Remove(old); old.Dispose(); }
        }
        if (duration <= 0) Clean();
        else
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration));
            fade.Completed += (_, _) => { if (revision == _revision) Clean(); };
            next.BeginAnimation(OpacityProperty, fade);
        }
    }

    public void Dispose()
    {
        _disposed=true;
        ++_revision; _key = null; _pending?.Dispose(); _pending = null;
        foreach (Layer layer in Children) layer.Dispose();
        Children.Clear();
    }

    private sealed class Layer : Grid, IDisposable
    {
        private ThemeProfile _profile;
        private MediaElement _video;
        private DispatcherTimer _timer;
        private bool _disposed;
        private readonly CancellationTokenSource _cancel = new();
        private TaskCompletionSource<bool> _opened;
        private Border _shade;
        private FrameworkElement _mediaVisual;
        private readonly GlassChrome _glass = new();
        public string Path => _profile.BackgroundPath;
        public Layer(ThemeProfile profile)
        {
            _profile = profile;
            Background = new SolidColorBrush(SurfaceColor(profile));
            if(string.IsNullOrWhiteSpace(profile.BackgroundPath))
            {
                Children.Add(_glass);
                ApplyMaterial(profile,0);
            }
            IsVisibleChanged += (_, _) =>
            {
                if (_disposed) return;
                if (IsVisible) { _video?.Play(); _timer?.Start(); }
                else { _video?.Pause(); _timer?.Stop(); }
            };
        }
        public void UpdateAppearance(ThemeProfile profile,double duration)
        {
            _profile=profile.Copy();
            var color=SurfaceColor(profile);
            var brush=new SolidColorBrush(color);
            if (Background is SolidColorBrush old && duration>0)
                brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(old.Color,color,TimeSpan.FromMilliseconds(duration)));
            Background=brush;
            if (_shade!=null)
            {
                _shade.Background=brush;
                double opacity=double.IsFinite(profile.BackgroundDim)?Math.Clamp(profile.BackgroundDim,0,1):.35;
                _shade.BeginAnimation(OpacityProperty,new DoubleAnimation(_shade.Opacity,opacity,TimeSpan.FromMilliseconds(duration)));
            }
            ApplyMaterial(profile,duration);
        }
        private static Color SurfaceColor(ThemeProfile profile)
        {
            return ThemeService.GlassSurfaceColor(profile);
        }
        private void ApplyMaterial(ThemeProfile profile,double duration)
        {
            bool glass=profile.Material == "Frosted";
            if(glass) _glass.SetProfile(profile);
            _glass.BeginAnimation(OpacityProperty,new DoubleAnimation(_glass.Opacity,glass?1:0,TimeSpan.FromMilliseconds(duration)));
        }
        public async Task LoadAsync()
        {
            string path = _profile.BackgroundPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!File.Exists(path)) throw new FileNotFoundException();
            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".mp4" or ".wmv" or ".avi" or ".mov" or ".m4v")
            {
                _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _video = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
                    IsMuted = true, Volume = 0, Stretch = Stretch.UniformToFill, ScrubbingEnabled = true };
                _video.MediaOpened += (_, _) => _opened.TrySetResult(true);
                _video.MediaFailed += (_, e) => _opened.TrySetException(e.ErrorException);
                _video.MediaEnded += (_, _) => { if (!_disposed) { _video.Position = TimeSpan.Zero; if (IsVisible) _video.Play(); } };
                Children.Add(_video);
                _mediaVisual=_video;
                _video.Source = new Uri(path); _video.Play();
                await _opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
                if (!IsVisible) _video.Pause();
            }
            else if (extension == ".gif")
            {
                var frames = await Task.Run(() => ReadGif(path,_cancel.Token));
                if (_disposed) return;
                var image = new Image { Source = frames[0].image, Stretch = Stretch.UniformToFill };
                Children.Add(image);
                _mediaVisual=image;
                int index = 0;
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(frames[0].delay) };
                _timer.Tick += (_, _) => { index = (index+1)%frames.Count; image.Source = frames[index].image; _timer.Interval = TimeSpan.FromMilliseconds(frames[index].delay); };
                if (IsVisible) _timer.Start();
            }
            else
            {
                var bitmap = await Task.Run(() =>
                {
                    var result = new BitmapImage(); result.BeginInit(); result.CacheOption = BitmapCacheOption.OnLoad;
                    result.DecodePixelWidth = 1920; result.UriSource = new Uri(path); result.EndInit(); result.Freeze(); return result;
                });
                if (_disposed) return;
                _mediaVisual=new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
                Children.Add(_mediaVisual);
            }
            if (!_disposed)
            {
                _shade=new Border { Background = new SolidColorBrush(ThemeService.Parse(_profile.Surface,"#FFFFFF")), Opacity = double.IsFinite(_profile.BackgroundDim) ? Math.Clamp(_profile.BackgroundDim,0,1) : .35 };
                Children.Add(_shade);
                Children.Add(_glass);
                ApplyMaterial(_profile,0);
            }
        }

        private static List<(BitmapSource image, int delay)> ReadGif(string path,CancellationToken cancellation)
        {
            // GDI+ composes GIF disposal rectangles before each frame is copied.
            using var source = System.Drawing.Image.FromFile(path);
            int count = source.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
            var delays = source.PropertyIdList.Contains(0x5100) ? source.GetPropertyItem(0x5100)?.Value : null;
            var scale = Math.Min(1d,Math.Min(1280d/source.Width,720d/source.Height));
            // Bound decoded memory for long animations, while retaining every frame.
            scale = Math.Min(scale,Math.Sqrt(96d*1024*1024/(Math.Max(1,count)*(double)source.Width*source.Height*4)));
            int width = Math.Max(1,(int)(source.Width*scale)), height = Math.Max(1,(int)(source.Height*scale));
            if (count > 3000) throw new InvalidDataException("GIF is too long");
            var frames = new List<(BitmapSource,int)>();
            for (int i=0;i<count;i++)
            {
                cancellation.ThrowIfCancellationRequested();
                source.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time,i);
                using var frame = new System.Drawing.Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var graphics = System.Drawing.Graphics.FromImage(frame)) graphics.DrawImage(source,0,0,width,height);
                var bits = frame.LockBits(new System.Drawing.Rectangle(0,0,width,height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    var bitmap = BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,bits.Scan0,bits.Stride*height,bits.Stride);
                    bitmap.Freeze();
                    int delay = delays != null && delays.Length >= (i+1)*4 ? Math.Max(20,BitConverter.ToInt32(delays,i*4)*10) : 100;
                    frames.Add((bitmap,delay));
                }
                finally { frame.UnlockBits(bits); }
            }
            return frames;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _cancel.Cancel(); _opened?.TrySetCanceled(); _timer?.Stop(); _timer=null;
            _video?.Close(); _video=null; _mediaVisual=null; Children.Clear();
        }
    }
}
