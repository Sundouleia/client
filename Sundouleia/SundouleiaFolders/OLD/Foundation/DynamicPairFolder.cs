using Sundouleia.Gui.Handlers;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.Components;

/// <summary>
///     An implementation of <see cref="DynamicFolder{TModel, TDrawEntity}"/> specifically for Sundesmos."/>
/// </summary>
public abstract class DynamicPairFolder : DynamicFolder<Sundesmo, DrawEntitySundesmo>
{
    protected readonly SundesmoManager _sundesmos;

    /// <summary>
    ///     You are expected to call RegenerateItems in any derived constructor to populate the folder contents.
    /// </summary>
    protected DynamicPairFolder(string label, FolderOptions options, ILogger log, 
        SundouleiaMediator mediator, FolderConfig config, DrawEntityFactory factory, 
        GroupsManager groups, SharedFolderMemory memory, SundesmoManager sundesmos)
        : base(label, options, log, mediator, config, factory, groups, memory)
    {
        _sundesmos = sundesmos;

        // Subscribe to pair-related changes here via the mediator calls.
        Mediator.Subscribe<FolderUpdateSundesmos>(this, _ => RegenerateItems(string.Empty));
    }

    public int Rendered => _allItems.Count(s => s.IsRendered);
    public int Online => _allItems.Count(s => s.IsOnline);

    protected override List<Sundesmo> GetAllItems() => _sundesmos.DirectPairs;
    protected override DrawEntitySundesmo ToDrawEntity(Sundesmo item) => _factory.CreateDrawEntity(this, item);


    protected override bool CheckFilter(Sundesmo u, string filter)
    {
        if (filter.IsNullOrEmpty()) return true;
        // return a user if the filter matches their alias or ID, playerChara name, or the nickname we set.
        return u.UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (u.GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (u.PlayerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    protected override List<DrawEntitySundesmo> ApplySortOrder(IEnumerable<DrawEntitySundesmo> source)
    {
        var builder = new FolderSortBuilder(source);
        foreach (var f in GetSortOrder())
            builder.Add(f);

        return builder.Build();
    }
}
