using Dalamud.Bindings.ImGui;
using OtterGui.Classes;
using OtterGui.Extensions;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.CustomCombos.Editor;

// A special combo for pairs, that must maintain its distinctness and update accordingly based on changes.
public sealed class PairCombo : CkFilterComboCache<Sundesmo>, IMediatorSubscriber, IDisposable
{
    private readonly FavoritesConfig _favorites;

    private Sundesmo? _currentUser;
    private bool _needsRefresh = false;
    public PairCombo(ILogger log, SundouleiaMediator mediator, MainConfig config, SundesmoManager pairs, FavoritesConfig favorites)
        : base(() => [
            ..pairs.DirectPairs
                .OrderByDescending(p => favorites.SundesmoUids.Contains(p.UserData.UID))
                .ThenByDescending(u => u.IsRendered)
                .ThenByDescending(u => u.IsOnline)
                .ThenBy(pair => !pair.PlayerName.IsNullOrEmpty()
                    ? (config.Current.PreferNicknamesOverNames ? pair.GetNickAliasOrUid() : pair.PlayerName)
                    : pair.GetNickAliasOrUid(), StringComparer.OrdinalIgnoreCase)
        ], log)
    {
        Mediator = mediator;
        _favorites = favorites;
        SearchByParts = true;

        Mediator.Subscribe<RefreshWhitelistMessage>(this, _ => _needsRefresh = true);
    }

    public SundouleiaMediator Mediator { get; }

    void IDisposable.Dispose()
    {
        Mediator.UnsubscribeAll(this);
        GC.SuppressFinalize(this);
    }

    private void UpdateCurrentSelection()
    {
        if (!_needsRefresh)
            return;

        Log.LogInformation("Performing PairCombo UpdateCurrentSelection");

        // if initialized, do a forced cleanup.
        var priorState = IsInitialized;
        if (priorState)
            Cleanup();

        // Update the Idx from the cache.
        CurrentSelectionIdx = Items.IndexOf(i => i.UserData.UID == Current?.UserData.UID);
        // if the index is a valid index, update the selection.
        if (CurrentSelectionIdx >= 0)
        {
            UpdateSelection(Items[CurrentSelectionIdx]);
        }
        else
        {
            UpdateSelection(null);
        }

        // If we were not in a prior state by this point, go ahead and cleanup.
        if (!priorState)
            Cleanup();
        // mark that we no longer need a refresh.
        _needsRefresh = false;
    }

    protected override bool IsVisible(int globalIndex, LowerString filter)
        => Items[globalIndex].UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (Items[globalIndex].GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
        || (Items[globalIndex].PlayerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    protected override string ToString(Sundesmo obj)
        => obj.GetNickAliasOrUid();

    protected override int UpdateCurrentSelected(int currentSelected)
    {
        CurrentSelectionIdx = Items.IndexOf(p => _currentUser == p);
        UpdateSelection(CurrentSelectionIdx >= 0 ? Items[CurrentSelectionIdx] : null);
        return CurrentSelectionIdx;
    }

    public void ClearSelected()
        => UpdateSelection(null);


    /// <summary> An override to the normal draw method that forces the current item to be the item passed in. </summary>
    /// <returns> True if a new item was selected, false otherwise. </returns>
    public bool Draw(Sundesmo? current, float width, float innerScalar = 1.25f, uint? searchBg = null)
        => Draw(current, width, innerScalar, CFlags.None, searchBg);

    public bool Draw(Sundesmo? current, float width, float innerScalar, CFlags flags, uint? searchBg = null)
    {
        _currentUser = current;
        // Update the currently selected User.
        // This allows it to process upon anything updating, regardless of interaction state.
        UpdateCurrentSelection();
        InnerWidth = width * innerScalar;
        var preview = Current?.GetNickAliasOrUid() ?? "Select Pair...";
        var ret = Draw("##PairCombo", preview, string.Empty, width, ImGui.GetTextLineHeightWithSpacing(), flags, searchBg);

        _currentUser = null;
        return ret;
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var sundesmo = Items[globalIdx];

        if(SundouleiaEx.DrawFavoriteStar(_favorites, sundesmo.UserData.UID, false) && CurrentSelectionIdx == globalIdx)
        {
            CurrentSelectionIdx = -1;
            Current = default;
        }

        ImUtf8.SameLineInner();
        var ret = ImGui.Selectable(ToString(sundesmo), selected);
        return ret;
    }

}
