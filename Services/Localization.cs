using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using System.Text.Json;
namespace LaunchPad.Services;
public sealed class AppLanguage : INotifyPropertyChanged
{
    public static AppLanguage Current { get; } = new();
    private static readonly Dictionary<string,string> English=JsonSerializer.Deserialize<Dictionary<string,string>>(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("LaunchPad.Locales.en.json"));
    public static string Language { get; private set; }="zh-CN";
    public event PropertyChangedEventHandler PropertyChanged;
    public string this[string key] => T(key);
    public static string T(string value) => Language=="en-US" && English.TryGetValue(value,out var text)?text:value;
    public static void SetLanguage(string language)
    {
        Language=language=="en-US"?"en-US":"zh-CN";
        Current.PropertyChanged?.Invoke(Current,new PropertyChangedEventArgs("Item[]"));
    }
}
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; }
    public LocExtension(string key) { Key=key; }
    public override object ProvideValue(IServiceProvider provider) => new Binding("["+Key+"]") { Source=AppLanguage.Current,Mode=BindingMode.OneWay }.ProvideValue(provider);
}
