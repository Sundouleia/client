namespace Sundouleia.Gui.Components;

public interface IDynamicFolder
{
    /// <summary>
    ///     Required to share memory between sundesmo folders.
    /// </summary>
    SharedFolderMemory SharedMemory { get; }

    /// <summary>
    ///     The Distinct Identifier label of this folder.
    /// </summary>
    string DistinctId { get; }

    /// <summary>
    ///     Display Label for the folder (can change).
    /// </summary>
    string Label { get; }

    /// <summary>
    ///     The DrawEntities contained within this folder.
    /// </summary>
    IReadOnlyList<IDrawEntity> DrawEntities { get; }

    /// <summary>
    ///     The total sundesmos within this folder.
    /// </summary>
    int Total { get; }

    /// <summary>
    ///     Draw the folder, (can be toggled), and its items if expanded.
    /// </summary>
    void DrawContents();

    /// <summary>
    ///     Simply draws out the folder. Not for toggling.
    /// </summary>
    void DrawFolder();

    /// <summary>
    ///     Draws the <see cref="DrawEntities"/> within this folder."/>
    /// </summary>
    void DrawItems();

    /// <summary>
    ///     Regenerate the 'DrawEntities' based on the provided filter."
    /// </summary>
    void RegenerateItems(string filter);

    /// <summary>
    ///     Update the filtered items based on the new provided filter.
    /// </summary>
    void UpdateItemsForFilter(string filter);
}

