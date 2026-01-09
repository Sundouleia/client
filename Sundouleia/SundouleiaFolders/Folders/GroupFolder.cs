using CkCommons.DrawSystem;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;

namespace Sundouleia.DrawSystem;

public sealed class GroupFolder : DynamicFolder<Sundesmo>
{
    // We store this to have a dynamically generated list without the need of a generator.
    private Func<IReadOnlyList<Sundesmo>> _generator;
    public GroupFolder(DynamicFolderGroup<Sundesmo> parent, uint id, SundesmoManager sundesmos, SundesmoGroup g)
        : base(parent, g.Icon, g.Label, id)
    {
        Group = g;
        // Define the generator.
        _generator = () => [.. sundesmos.DirectPairs.Where(u => g.LinkedUids.Contains(u.UserData.UID) && (g.ShowOffline || u.IsOnline))];
        // Apply Stylizations.
        ApplyLatestStyle();
        // Set initial unsorted steps.
        UnusedSteps = SorterExtensions.AllGroupSteps.Except(Sorter).ToList();
    }

    public GroupFolder(DynamicFolderGroup<Sundesmo> parent, uint id, SundesmoManager sundesmos, 
        SundesmoGroup g, IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> sortSteps)
        : base(parent, g.Icon, g.Label, id, new(sortSteps))
    {
        // Store the group.
        Group = g;
        // Define the generator.
        _generator = () => ShowOffline 
            ? [.. sundesmos.DirectPairs.Where(u => g.LinkedUids.Contains(u.UserData.UID))]
            : [.. sundesmos.DirectPairs.Where(u => g.LinkedUids.Contains(u.UserData.UID) && u.IsOnline)];
        // Apply Stylizations.
        ApplyLatestStyle();
        // Set initial unsorted steps.
        UnusedSteps = SorterExtensions.AllGroupSteps.Except(Sorter).ToList();
    }

    internal SundesmoGroup Group;
    public bool ShowOffline => Group.ShowOffline;
    public int Rendered => GetChildren().Count(s => s.Data.IsRendered);
    public int Online => Children.Count(s => s.Data.IsOnline);
    protected override IReadOnlyList<Sundesmo> GetAllItems() => _generator();
    protected override DynamicLeaf<Sundesmo> ToLeaf(Sundesmo item) => new(this, item.UserData.UID, item);

    public IReadOnlyDynamicSorter<DynamicLeaf<Sundesmo>> FolderSorter => Sorter;

    /// <summary>
    ///     Manually updated on every sort order update. <para />
    ///     Used to help boost draw performance for the filter editor.
    /// </summary>
    public IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> UnusedSteps = [];

    /// <summary>
    ///     Update only the folder styles. <b> Leave Name and SortOrder as is.</b> <para />
    ///     Nothing internally is updated so no refresh or save is necessary.
    /// </summary>
    public void ApplyLatestStyle()
    {
        Icon = Group.Icon;
        IconColor = Group.IconColor;
        NameColor = Group.LabelColor;
        BorderColor = Group.BorderColor;
        GradientColor = Group.GradientColor;
    }

    /// <summary>
    ///     Updates the SortOrder in the GroupFolder via the SortOrder in SundesmoGroup. <para />
    ///     You are expected to execute a refresh after this somewhere if ever called.
    /// </summary>
    public void ApplyLatestSorter()
    {
        // Retrieve all expected sort steps.
        var all = SorterExtensions.AllGroupSteps;
        // Fetch the new sort order from the group.
        var desired = Group.SortOrder.Select(f => f.ToSortMethod());
        // Update the Folders sorter to the new steps.
        Sorter.SetSteps(desired);
        // Update the unused steps for the filter editor.
        UnusedSteps = all.Except(desired).ToList();
    }
}