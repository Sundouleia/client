using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Sundouleia.DrawSystem.Selector;

// Initialization and base calls for the selector.
public partial class DynamicDrawer<T> : IDisposable where T : class
{
    protected readonly ILogger Log;
    protected readonly DynamicDrawSystem<T>  DrawSystem;
    protected readonly DynamicFilterCache<T> Cache;
    protected readonly DynamicSelections<T>  Selector;
    protected readonly DynamicDragDrop<T>    DragDrop;

    // Queue of all actions to perform after completely drawing the list,
    // to avoid processing operations that would modify the filter mid-draw. (Can be reworked later if we need to)
    private readonly Queue<Action> _postDrawActions = new();

    public DynamicDrawer(string label, ILogger log, DynamicDrawSystem<T> drawSystem, 
        DynamicFilterCache<T>? cache = null)
    {
        Label = label;

        Log        = log;
        DrawSystem = drawSystem;
        Cache      = cache ?? new(drawSystem);
        Selector   = new(drawSystem, Cache);
        DragDrop   = new(this, Selector);
    }

    public string Label { get; protected set; } = string.Empty;

    public virtual void Dispose()
    {
        Selector.Dispose();
        Cache.Dispose();
    }

    // Manages all nodes hover state over a 2 setters per draw-frame outside updates.
    protected IDynamicNode? _hoveredNode = null; // From last frame.
    protected IDynamicNode? _newHoveredNode = null; // Tracked each frame.

    /// <summary>
    ///     Draws the full hierarchy of a DynamicDrawSystem's Root folder. <para />
    ///     Can define the width of this display, and the flags.
    /// </summary>
    public void DrawFullCache(float width, DynamicFlags flags = DynamicFlags.None)
    {
        using var _ = ImRaii.Child(Label, new Vector2(width, -1), false, WFlags.NoScrollbar);
        if (!_)
            return;

        HandleMainContextActions();
        Cache.UpdateCache();

        // Set the style for the draw logic.
        ImGui.SetScrollX(0);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One)
             .Push(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale);

        DrawFolderGroupChildren(Cache.RootCache, flags);
        PostDraw();
    }

    /// <inheritdoc cref="DrawFullCache(float, DynamicFlags)"/>
    /// <remarks> Only <see cref="DynamicFolder{T}"/>'s of type <typeparamref name="TFolder"/> are drawn. </remarks>
    public void DrawFullCache<TFolder>(float width, DynamicFlags flags = DynamicFlags.None) where TFolder : DynamicFolder<T>
    {
        using var _ = ImRaii.Child(Label, new Vector2(width, -1), false, WFlags.NoScrollbar);
        if (!_)
            return;

        HandleMainContextActions();
        Cache.UpdateCache();

        // Set the style for the draw logic.
        ImGui.SetScrollX(0);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One)
             .Push(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale);

        DrawFolderGroupChildren<TFolder>(Cache.RootCache, flags);
        PostDraw();
    }

    /// <summary>
    ///     Attempts to draw a IDynamicCache Folder or FolderGroup by its name. <para />
    ///     Can provide width and flags for the draw. <para />
    ///     <b> Cannot be whitelisted to a single folder type. No reason to. </b>
    /// </summary>
    /// <param name="folderName"> The name of the folder to draw. </param>
    /// <param name="width"> The width of the draw area. </param>
    /// <param name="flags"> The flags to use for drawing. </param>
    /// <remarks> If the folder is not found, nothing is drawn. </remarks>
    public void DrawFolder(string folderName, float width, DynamicFlags flags = DynamicFlags.None)
    {
        using var _ = ImRaii.Child(Label, new Vector2(width, -1), false, WFlags.NoScrollbar);
        if (!_)
            return;

        HandleMainContextActions();
        Cache.UpdateCache();

        if (!DrawSystem.TryGetFolder(folderName, out var node))
            return;
        if (!Cache.CacheMap.TryGetValue(node, out var cachedNode))
            return;

        // Set the style for the draw logic.
        ImGui.SetScrollX(0);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One)
             .Push(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale);

        DrawClippedCacheNode(cachedNode, flags);
        PostDraw();
    }

    /// <summary>
    ///     Attempts to draw a IDynamicCache Folder or FolderGroup by its IDynamicCollection counterpart. <para />
    ///     <b> Cannot be whitelisted to a single folder type. No reason to. </b>
    /// </summary>
    /// <param name="folder"> The Folder we want to draw the cached version of </param>
    /// <param name="width"> The width of the draw area. </param>
    /// <param name="flags"> The flags to use for drawing. </param>
    /// <remarks> If the folder is not found, nothing is drawn. </remarks>
    public void DrawFolder(IDynamicCollection<T> folder, float width, DynamicFlags flags = DynamicFlags.None)
    {
        // Ensure the child is at least draw to satisfy the expected drawn content region.
        using var _ = ImRaii.Child(Label, new Vector2(width, -1), false, WFlags.NoScrollbar);
        if (!_)
            return;

        // Handle any main context interactions such as right-click menus and the like.
        HandleMainContextActions();
        // Update the cache to its latest state.
        Cache.UpdateCache();

        if (!Cache.CacheMap.TryGetValue(folder, out var cachedNode))
            return;

        // Set the style for the draw logic.
        ImGui.SetScrollX(0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One)
            .Push(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale);
        // Draw out the node, for folders only.
        DrawClippedCacheNode(cachedNode, flags);
        PostDraw();
    }

    /// <summary>
    ///     Add post-draw logic that is executed after drawing the full DynamicDrawSelector UI. <para />
    ///     Reserved for operations that would modify the cache state, or set the filter to dirty. <para />
    ///     If you override this make sure that the base is called, or else post-draw actions will not be processed.
    /// </summary>
    protected void PostDraw()
    {
        UpdateHoverNode();
        ImGui.Text($"Selected: {Selector.Collections.Count} Collections");
        ImGui.Text($"Selected: {Selector.Leaves.Count} Leaves");
        ImGui.Text($"Selected: {Selector.Selected.Count} Nodes");
        ImGui.Text($"CacheMapSize: {Cache.CacheMap.Count}");
        ImGui.Text($"FlatCacheSize: {Cache.FlatList.Count}");
        ImGui.Text($"Total Cache Children: {Cache.RootCache.GetAllDescendants().Count()}");
        ImGui.Text($"Total DragDrop Nodes: {DragDrop.Total}");
        ImGui.Text($"DragDrop Names: {string.Join(',', DragDrop.Nodes.Select(n => n.Name))}");
        // Process post-draw actions.
        while (_postDrawActions.TryDequeue(out Action? action))
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.LogWarning($"HandleAction Error: {e}");
            }
        }
    }

    protected virtual void UpdateHoverNode()
    {
        _hoveredNode    = _newHoveredNode;
        _newHoveredNode = null;
    }



    // Helper for parent classes.
    protected void AddPostDrawLogic(Action act)
        => _postDrawActions.Enqueue(act);
}
