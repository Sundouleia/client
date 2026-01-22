using Dalamud.Bindings.ImGui;
using OtterGui.Classes;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.CustomCombos.Editor;

// A special combo for pairs, that must maintain its distinctness and update accordingly based on changes.
public sealed class SundesmoForGroupCombo : CkFilterComboCache<Sundesmo>, IMediatorSubscriber, IDisposable
{
    private readonly FavoritesConfig _favorites;
    // Temporarily cached during drawtime to filter visible items.
    private SundesmoGroup _drawnGroup = new();
    public SundesmoForGroupCombo(ILogger log, SundouleiaMediator mediator, FavoritesConfig favorites, Func<IReadOnlyList<Sundesmo>> gen)
        : base(gen, log)
    {
        Mediator = mediator;
        _favorites = favorites;
        SearchByParts = true;

        Mediator.Subscribe<FolderUpdateSundesmos>(this, _ => Cleanup());
    }

    public SundouleiaMediator Mediator { get; }

    void IDisposable.Dispose()
    {
        Mediator.UnsubscribeAll(this);
        GC.SuppressFinalize(this);
    }

    protected override bool IsVisible(int globalIndex, LowerString filter)
    {
        if (_drawnGroup.LinkedUids.Contains(Items[globalIndex].UserData.UID))
            return false;

        return Items[globalIndex].UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (Items[globalIndex].GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
        || (Items[globalIndex].PlayerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    protected override string ToString(Sundesmo obj)
        => obj.GetDisplayName();

    /// <summary> An override to the normal draw method that forces the current item to be the item passed in. </summary>
    /// <returns> True if a new item was selected, false otherwise. </returns>
    public bool Draw(SundesmoGroup group, float width, float innerScalar = 1.25f)
        => Draw(group, width, innerScalar, CFlags.None);

    public bool Draw(SundesmoGroup group, float width, float innerScalar, CFlags flags)
    {
        _drawnGroup = group;
        InnerWidth = width * innerScalar;
        return Draw("##PairCombo", "Add Users To Group...", string.Empty, width, ImUtf8.FrameHeightSpacing, flags);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var sundesmo = Items[globalIdx];
        var size = new Vector2(GetFilterWidth(), ImUtf8.FrameHeight);

        if (SundouleiaEx.DrawFavoriteStar(_favorites, sundesmo.UserData.UID, false) && CurrentSelectionIdx == globalIdx)
        {
            CurrentSelectionIdx = -1;
            Current = default;
        }
        ImUtf8.SameLineInner();
        var ret = ImGui.Selectable(ToString(sundesmo), selected);
        return ret;
    }

}
