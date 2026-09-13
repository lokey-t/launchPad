using System.Globalization;
using LaunchPad.Models;

namespace LaunchPad.Services;

public enum EntrySortMode { Added, Name, Opened }

public static class EntrySortService
{
    public static List<AppEntry> Order(IEnumerable<AppEntry> entries, EntrySortMode mode)
    {
        // LINQ ordering is stable: ties and unknown legacy timestamps retain their existing relative order.
        return mode switch
        {
            EntrySortMode.Added => entries.OrderByDescending(e => e.AddedAt).ToList(),
            EntrySortMode.Opened => entries.OrderByDescending(e => e.LastOpenedAt).ToList(),
            _ => entries.OrderBy(e => e.Name ?? "", StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true)).ToList()
        };
    }

    public static void Apply(LauncherConfig config, AppCategory category, EntrySortMode mode)
    {
        EntryMoveService.NormalizeOrder(config);
        var byId = config.Categories.SelectMany(c => c.Entries).ToDictionary(e => e.Id);
        var source = category == null ? config.GlobalOrder.Select(id => byId[id]).ToList() : category.Entries.ToList();
        var sorted = Order(source,mode);
        if (category == null) config.GlobalOrder = sorted.Select(e => e.Id).ToList();
        else
        {
            var ids = sorted.Select(e => e.Id).ToHashSet();
            int index = 0;
            config.GlobalOrder = config.GlobalOrder.Select(id => ids.Contains(id) ? sorted[index++].Id : id).ToList();
        }
        var rank = config.GlobalOrder.Select((id,i) => (id,i)).ToDictionary(p => p.id,p => p.i);
        foreach (var owner in config.Categories.Where(c => category == null || c == category))
        {
            var order = owner.Entries.OrderBy(e => rank[e.Id]).ToList();
            for (int i = 0; i < order.Count; i++) owner.Entries.Move(owner.Entries.IndexOf(order[i]),i);
        }
    }
}
