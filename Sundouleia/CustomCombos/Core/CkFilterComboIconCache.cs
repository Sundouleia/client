using OtterGui.Classes;

namespace Sundouleia.CustomCombos;

/// <summary> 
///     Similar to the FilterComboCache, except for Gallery bases. May become entirely unused.
/// </summary>
public abstract class CkFilterComboGalleryCache<T> : CkFilterComboGallery<T>
{
    /// <summary> The selected item in non-index format. </summary>
    /// <remarks> This is for the OPENED Combo. This means if a combo has multiple draws, only the focused list reflects this. <remarks>
    public T? Current { get; protected set; }

    /// <summary> A Cached List of the generated items. </summary>
    /// <remarks> Items are regenerated every time a cleanup is called. </remarks>
    private readonly ICachingList<T> _items;

    /// <summary> The current selection index in the filter cache. </summary>
    /// <remarks> This is for the OPENED Combo. This means if a combo has multiple draws, only the focused list reflects this. <remarks>
    protected int CurrentSelectionIdx = -1;

    /// <summary> The condition that is met whenever the CachingList <typeparamref name="T"/> has finished caching the generated item function. </summary>
    protected bool IsInitialized => _items.IsInitialized;

    protected CkFilterComboGalleryCache(IEnumerable<T> items, Vector2 itemSize, ILogger log)
        : base(new TemporaryList<T>(items), itemSize, log)
    {
        Current = default(T);
        _items = (ICachingList<T>)Items;
    }

    protected CkFilterComboGalleryCache(Func<IReadOnlyList<T>> generator, Vector2 itemSize, ILogger log)
        : base(new LazyList<T>(generator), itemSize, log)
    {
        Current = default(T);
        _items = (ICachingList<T>)Items;
    }

    /// <summary> Triggers our Caching list to regenerate its passed in item list. </summary>
    /// <remarks> Call this whenever the source of our list updates to keep it synced. </remarks>
    protected override void Cleanup()
        => _items.ClearList();

    /// <summary> Draws the list and updates the selection in the filter cache if needed. </summary>
    protected override void DrawGallery(float innerWidth, float galleryHeight)
    {
        base.DrawGallery(innerWidth, galleryHeight);
        if (NewSelection != null && Items.Count > NewSelection.Value)
            UpdateSelection(Items[NewSelection.Value]);
    }

    /// <summary> Invokes SelectionChanged & updates Current. </summary>
    /// <remarks> Called if a change occurred in the DrawList override. </remarks>
    protected virtual void UpdateSelection(T? newSelection)
    {
        Current = newSelection;
    }

    /// <summary> The main Draw function that should be used for any parenting client side FilterCombo's of all types. </summary>
    /// <remarks> Any selection, or any change, will be stored into the CurrentSelectionIdx. </remarks>
    public bool Draw(string label, string preview, float previewWidth, CFlags flags = CFlags.None, uint? customSearchBg = null)
    {
        return Draw(label, preview, previewWidth, ref CurrentSelectionIdx, flags, customSearchBg);
    }

    public bool DrawPopup(string label, Vector2 openPos, uint? customSearchBg = null)
        => DrawPopup(label, openPos, ref CurrentSelectionIdx, customSearchBg);
}
