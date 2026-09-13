using System.Windows;
using System.Windows.Controls;
using LaunchPad.Services;

namespace LaunchPad;

public partial class MainWindow
{
    private MenuItem CreateSortingMenu()
    {
        var menu = new MenuItem { Header=AppLanguage.T("排序"), Icon="↕" };
        foreach (var (label, mode) in new[] { ("添加时间",EntrySortMode.Added), ("首字母",EntrySortMode.Name), ("打开时间",EntrySortMode.Opened) })
        {
            var item = new MenuItem { Header=AppLanguage.T(label), Tag=mode.ToString() };
            item.Click += SortEntries_Click;
            menu.Items.Add(item);
        }
        return menu;
    }

    private void SortEntries_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_dragging || _externalDragging || sender is not MenuItem option || !Enum.TryParse<EntrySortMode>(option.Tag as string,out var mode)) return;
        var category = _activeTab?.Category;
        EntrySortService.Apply(_app.Config,category,mode);
        ConfigService.Save(_app.Config);
        ReloadFromConfig();
    }
}
