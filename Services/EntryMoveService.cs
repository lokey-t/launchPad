using LaunchPad.Models;

namespace LaunchPad.Services;

/// <summary>Commit a move only when a drop is accepted; hovering never changes ownership.</summary>
public static class EntryMoveService
{
    public static AppCategory Owner(LauncherConfig config, AppEntry entry) => config.Categories
        .FirstOrDefault(c => c.Entries.Contains(entry) || c.Entries.OfType<AppFolder>()
            .Any(f => entry is AppItem item && f.Items.Contains(item)));

    public static bool IntoFolder(LauncherConfig config, AppEntry entry, AppFolder folder, int index)
    {
        if (entry is not AppItem item || Owner(config, folder) == null) return false;
        if (folder.Items.Any(i => i != item && string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase))) return false;
        int previous = folder.Items.IndexOf(item);
        if (previous >= 0 && previous < index) index--;
        Detach(config, item, folder);
        folder.Items.Insert(Math.Clamp(index, 0, folder.Items.Count), item);
        folder.RefreshThumb();
        NormalizeOrder(config);
        return true;
    }

    public static bool IntoCategory(LauncherConfig config, AppEntry entry, AppCategory category, int index, int? globalIndex = null)
    {
        if (!config.Categories.Contains(category)) return false;
        NormalizeOrder(config);
        var order = config.GlobalOrder.ToList();
        int oldGlobal = order.IndexOf(entry.Id);
        int previous = category.Entries.IndexOf(entry);
        if (previous >= 0 && previous < index) index--;
        Detach(config, entry, null);
        category.Entries.Insert(Math.Clamp(index, 0, category.Entries.Count), entry);
        if (globalIndex is int destination)
        {
            if (oldGlobal >= 0 && oldGlobal < destination) destination--;
            order.Remove(entry.Id);
            order.Insert(Math.Clamp(destination, 0, order.Count), entry.Id);
            config.GlobalOrder = order;
        }
        NormalizeOrder(config);
        return true;
    }

    private static void Detach(LauncherConfig config, AppEntry entry, AppFolder keep)
    {
        foreach (var category in config.Categories)
        {
            category.Entries.Remove(entry);
            foreach (var folder in category.Entries.OfType<AppFolder>().ToList())
            {
                if (entry is not AppItem item || !folder.Items.Remove(item)) continue;
                folder.RefreshThumb();
                if (folder.Items.Count == 0 && folder != keep) category.Entries.Remove(folder);
            }
        }
    }

    public static void NormalizeOrder(LauncherConfig config)
    {
        var ids = config.Categories.SelectMany(c => c.Entries).Select(e => e.Id).ToList();
        config.GlobalOrder = config.GlobalOrder.Where(ids.Contains).Concat(ids).Distinct().ToList();
    }
}
