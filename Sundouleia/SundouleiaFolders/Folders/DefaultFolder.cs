using CkCommons.DrawSystem;
using Dalamud.Bindings.ImGui;
using Sundouleia.Pairs;

namespace Sundouleia.DrawSystem;

public sealed class DefaultFolder : DynamicFolder<Sundesmo>
{
    private Func<IReadOnlyList<Sundesmo>> _generator;
    public DefaultFolder(DynamicFolderGroup<Sundesmo> parent, uint id, FAI icon, string name,
        uint iconColor, Func<IReadOnlyList<Sundesmo>> generator)
        : base(parent, icon, name, id)
    {
        // Can set stylizations here.
        NameColor = uint.MaxValue;
        IconColor = iconColor;
        BgColor = uint.MinValue;
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        GradientColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    public DefaultFolder(DynamicFolderGroup<Sundesmo> parent, uint id, FAI icon, string name,
        uint iconColor, Func<IReadOnlyList<Sundesmo>> generator, IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> sortSteps)
        : base(parent, icon, name, id, new(sortSteps))
    {
        // Can set stylizations here.
        NameColor = uint.MaxValue;
        IconColor = iconColor;
        BgColor = uint.MinValue;
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    public DefaultFolder(DynamicFolderGroup<Sundesmo> parent, uint id, FAI icon, string name,
        uint iconColor, Func<IReadOnlyList<Sundesmo>> generator, DynamicSorter<DynamicLeaf<Sundesmo>> sorter)
        : base(parent, icon, name, id, sorter)
    {
        // Can set stylizations here.
        NameColor = uint.MaxValue;
        IconColor = iconColor;
        BgColor = uint.MinValue;
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    public int Rendered => Children.Count(s => s.Data.IsRendered);
    public int Online => Children.Count(s => s.Data.IsOnline);
    protected override IReadOnlyList<Sundesmo> GetAllItems() => _generator();
    protected override DynamicLeaf<Sundesmo> ToLeaf(Sundesmo item) => new(this, item.UserData.UID, item);

    // Maybe replace with something better later. Would be nice to not depend on multiple generators but idk.
    public string BracketText => Name switch
    {
        Constants.FolderTagAll => $"[{TotalChildren}]",
        Constants.FolderTagVisible => $"[{Rendered}]",
        Constants.FolderTagOnline => $"[{Online}]",
        Constants.FolderTagOffline => $"[{TotalChildren}]",
        _ => string.Empty,
    };

    public string BracketTooltip => Name switch
    {
        Constants.FolderTagAll => $"{TotalChildren} total",
        Constants.FolderTagVisible => $"{Rendered} visible",
        Constants.FolderTagOnline => $"{Online} online",
        Constants.FolderTagOffline => $"{TotalChildren} offline",
        _ => string.Empty,
    };

    /// <summary>
    ///     Updates the SortOrder in the GroupFolder via the SortOrder in SundesmoGroup. <para />
    ///     You are expected to execute a refresh after this somewhere if ever called.
    /// </summary>
    public void ApplySorter(IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> sortSteps)
        => Sorter.SetSteps(sortSteps);
}