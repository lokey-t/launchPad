using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LaunchPad;
using LaunchPad.Models;
using LaunchPad.Services;

class IsolatedApp : App
{
    protected override void OnStartup(StartupEventArgs e) { }
    protected override void OnExit(ExitEventArgs e) { }
}
static class UpdateUiTests
{
    static void Pump(int ms) { var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame); }
    static void Assert(bool ok,string message) { if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message); }
    static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
    static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Capture(Window window,string name)
    {
        Directory.CreateDirectory("obj/update-shots");
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create("obj/update-shots/"+name+".png");encoder.Save(stream);
    }
    public static void Run()
    {
        var app=new IsolatedApp { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        foreach(string file in new[]{"Light","Menus","Scrollbars"}) app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri($"/LaunchPad;component/Themes/{file}.xaml",UriKind.Relative) });
        string data=Path.Combine(Path.GetTempPath(),"LaunchPad-UpdateUi-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        typeof(ConfigService).GetProperty("DataDirectory",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,data);
        var config=new LauncherConfig();typeof(App).GetProperty("Config").SetValue(app,config);
        var offer=new UpdateOffer(new Version(1,1,0,0),new(){new("GitHub","v1.1",new Version(1,1,0,0),"优化文件夹开启与关闭动画\n新增主题配置与应用更新\n改善拖动交互和视觉细节",new(){new(UpdateService.ManifestName,"https://github.com/lokey-t/launchPad/releases/download/v1.1/manifest.json",null)},1)});
        var window=new UpdateWindow(app,new UpdateService(),offer);window.Show();Pump(400);
        Assert(Field<Button>(window,"_update").Content.ToString()=="立即更新","incremental default action");
        Click(Field<Button>(window,"_manual"));Pump(150);
        Assert(Field<StackPanel>(window,"_sources").IsVisible && Field<StackPanel>(window,"_sources").Children.Count==2,"manual source chooser");
        Capture(window,"light-zh");Click(Field<Button>(window,"_ignore"));Pump(250);
        Assert(!window.IsVisible && ConfigService.Load().IgnoredUpdateVersion==offer.Version.ToString(),"ignore version persists and closes");
        AppLanguage.SetLanguage("en-US");config.Language="en-US";config.Theme="Dark";
        app.Resources.MergedDictionaries[0].Source=new Uri("/LaunchPad;component/Themes/Dark.xaml",UriKind.Relative);
        var legacy=new UpdateOffer(offer.Version,new(){offer.Mirrors[0] with {Assets=new(),Notes="Smoother folder transitions\nImproved appearance and interaction\nAutomatic updates with mirror failover"}});
        window=new UpdateWindow(app,new UpdateService(),legacy);window.Show();Pump(400);
        Assert(Field<Button>(window,"_update").Content.ToString()=="Full package update","legacy fallback and English labels");
        Capture(window,"dark-en");Click(Field<Button>(window,"_later"));Pump(250);
        Assert(!window.IsVisible,"later closes without updating");
        config.GlobalTheme=ThemeService.Global(config).Copy();config.GlobalTheme.Material="Frosted";
        window=new UpdateWindow(app,new UpdateService(),offer);window.Show();Pump(500);Capture(window,"frosted-en");
        Assert(((Border)window.Content).CornerRadius.TopLeft==8,"glass corner matches native material surface");Click(Field<Button>(window,"_later"));Pump(250);
        var handler=new Handler { Respond=_=>new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new[]{new {tag_name="v1.1",assets=Array.Empty<object>()}})) } };
        typeof(App).GetField("_updates",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(app,new UpdateService(new HttpClient(handler)));
        Task Check(bool manual)
        {
            var operation=app.Dispatcher.InvokeAsync(()=>app.CheckUpdatesAsync(manual));
            while(!operation.Task.IsCompleted) Pump(20);
            var task=operation.Task.Result;while(!task.IsCompleted) Pump(20);task.GetAwaiter().GetResult();return task;
        }
        UpdateWindow Active()=>(UpdateWindow)typeof(App).GetField("_updateWindow",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);
        Check(false);Assert(Active()==null,"ignored release suppresses automatic prompt");
        Check(true);Pump(300);Assert(Active()!=null,"manual check bypasses ignored release");
        Click(Field<Button>(Active(),"_later"));Pump(250);config.IgnoredUpdateVersion=null;
        Check(false);Assert(Active()==null,"later suppresses repeat prompts in same session");
        app.Shutdown();Console.WriteLine("UPDATE UI CHECKS PASSED");
    }
}
