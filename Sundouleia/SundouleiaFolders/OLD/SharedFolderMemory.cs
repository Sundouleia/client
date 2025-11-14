namespace Sundouleia.Gui.Components;

/// <summary>
///     Shared Memory container used by all created DrawFolder instances.
///     Helps properly send drag-drop data across folders properly.
/// </summary>
public class SharedFolderMemory(ILogger<SharedFolderMemory> logger) : IDisposable
{
    public static string MoveLabel = "SUNDOULEIA_DRAGDROP_FOLDER_MOVE";
    // Information about the data help in transit by the source being dragged.
    private IDynamicFolder? _sourceFolder;
    private List<IDrawEntity>? _selections;
    private Action? _onSourceTransferred;

    public void Dispose() => ClearPayloadMemory();

    /// <summary>
    /// Register a source and a copy of its current selection. This makes the registering folder
    /// the single active source until the payload is claimed or cleared.
    /// </summary>
    public void UpdateSourceCache(IDynamicFolder source, List<IDrawEntity> selections)
    {
        // Nothing changed during drag.
        if (_sourceFolder == source && selections.Count.Equals(_selections?.Count))
            return;

        logger.LogDebug($"Setting drag-drop in folder {source} with {selections.Count} selections.");
        _sourceFolder = source;
        _selections = selections;
    }


    public (IDynamicFolder Source, List<IDrawEntity> Transferred)? GetSourcePayload()
    {
        if (_sourceFolder is null || _selections is null)
            return null;

        logger.LogDebug($"Getting drag-drop source payload from folder {_sourceFolder.Label}.");
        _onSourceTransferred?.Invoke();
        return (_sourceFolder, _selections);
    }

    public void ClearPayloadMemory()
    {
        logger.LogDebug("Clearing drag-drop payload memory.");
        if (_selections is not null)
            _selections.Clear();
        _sourceFolder = null;
    }
}
