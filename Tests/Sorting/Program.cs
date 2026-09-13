using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LaunchPad;
using LaunchPad.Models;
using LaunchPad.Services;

class TestApp : App { protected override void OnStartup(StartupEventArgs e) {} protected override void OnExit(ExitEventArgs e) {} }
static class Program
{
    static void Assert(bool condition,string message) { if(!condition) throw new Exception(message);Console.WriteLine("PASS "+message); }
    static void Pump(int ms) { var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};t.Tick+=(_,_)=>{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f); }
    [STAThread] static void Main(string[] args)
    {
        try { Run(args.Contains("--ui")); } catch(Exception e) {Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
    static void Run(bool ui)
    {
        var config=new LauncherConfig();var a=config.GetOrCreateCategory("A");var b=config.GetOrCreateCategory("B");
        var old=new AppItem{Name="Zulu",AddedAt=DateTimeOffset.UtcNow.AddDays(-1)};
        var newer=new AppItem{Name="alpha",AddedAt=DateTimeOffset.UtcNow,LastOpenedAt=DateTimeOffset.UtcNow};
        var unknown=new AppItem{Name="Beta"};var untouched=new AppFolder{Name="Folder",Items=new(){new AppItem{Name="Child"}}};
        a.Entries.Add(old);a.Entries.Add(newer);a.Entries.Add(unknown);b.Entries.Add(untouched);
        config.GlobalOrder=new(){old.Id,untouched.Id,newer.Id,unknown.Id};
        EntrySortService.Apply(config,a,EntrySortMode.Added);
        Assert(a.Entries.SequenceEqual(new[]{newer,old,unknown}),"newest added first, unknown legacy timestamps last");
        Assert(config.GlobalOrder[1]==untouched.Id && b.Entries.Single()==untouched,"category sort preserves other categories and global slots");
        EntrySortService.Apply(config,null,EntrySortMode.Name);
        Assert(config.GlobalOrder.SequenceEqual(new[]{newer.Id,unknown.Id,untouched.Id,old.Id}),"global name sorting, ignoring case");
        Assert(untouched.Items.Single().Name=="Child","sorting keeps folder contents and category ownership");
        EntrySortService.Apply(config,a,EntrySortMode.Opened);
        Assert(a.Entries.First()==newer && a.Entries.Skip(1).SequenceEqual(new[]{unknown,old}),"recently opened first and stable unknown order");
        var loaded=ConfigService.Deserialize(ConfigService.Serialize(config));
        Assert(loaded.Categories[0].Entries[0].LastOpenedAt==newer.LastOpenedAt && loaded.Categories[0].Entries[1].AddedAt==null,"timestamps survive serialization without inventing legacy history");
        Assert(!AppLaunchService.Launch(unknown) && unknown.LastOpenedAt==null,"invalid launch does not record opening time");
        if(!ui) return;
        string root=Path.Combine(Path.GetTempPath(),"LaunchPad-SortTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        typeof(ConfigService).GetProperty("DataDirectory",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,root);
        var app=new TestApp{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        typeof(App).GetProperty("Config").SetValue(app,config);
        foreach(string file in new[]{"Light","Menus","Scrollbars"}) app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri($"/LaunchPad;component/Themes/{file}.xaml",UriKind.Relative)});
        var window=new MainWindow(app);window.Show();Pump(180);window.SelectCategory(a);Pump(250);
        typeof(MainWindow).GetMethod("SortEntries_Click",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{new MenuItem{Tag="Name"},new RoutedEventArgs(MenuItem.ClickEvent)});Pump(250);
        Assert(a.Entries.Select(e=>e.Name).SequenceEqual(new[]{"alpha","Beta","Zulu"}),"homepage sort handler reorders visible category");
        Assert(ConfigService.Load().Categories[0].Entries[1].Name=="Beta","sorted order persists to disk");
        window.Hide();
        foreach(string theme in new[]{"Light","Dark"})
        {
            config.Theme=theme;config.GlobalTheme=null;
            app.Resources.MergedDictionaries[0].Source=new Uri($"/LaunchPad;component/Themes/{theme}.xaml",UriKind.Relative);
            typeof(App).GetMethod("ShowTrayMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(app,null);Pump(250);
            var host=(Window)typeof(App).GetField("_trayMenuHost",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);
            var menu=((Border)host.Content).ContextMenu;
            Assert(menu.IsOpen && menu.Items.Count==4 && menu.ActualWidth>100,"styled tray menu opens: "+theme);
            Directory.CreateDirectory("obj/menu-shots");var image=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(menu);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create("obj/menu-shots/tray-"+theme+".png"))encoder.Save(output);
            menu.IsOpen=false;Pump(500);
            Assert(typeof(App).GetField("_trayMenuHost",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app)==null,"tray menu owner closes cleanly");
        }
        app.Shutdown();
    }
}


