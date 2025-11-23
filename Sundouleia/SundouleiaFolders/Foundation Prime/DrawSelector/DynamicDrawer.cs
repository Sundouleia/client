using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;

namespace Sundouleia.DrawSystem.Selector;

// Initialization and base calls for the selector.
public partial class DynamicDrawer<T> : IDisposable where T : class
{
    protected readonly ILogger Log;
    protected readonly DynamicDrawSystem<T> DrawSystem;

    // Queue of all actions to perform after completely drawing the list,
    // to avoid processing operations that would modify the filter mid-draw. (Can be reworked later if we need to)
    private readonly Queue<Action> _postDrawActions = new();

    protected string Label = string.Empty;

    public DynamicDrawer(string label, ILogger log, DynamicDrawSystem<T> drawSystem)
    {
        DrawSystem = drawSystem;
        Log        = log;
        Label      = label;

        // Placeholder for state until it is moved to its own managed cache. (may or may not do)
        _nodeCache = new CachedFolderGroup<T>(drawSystem.Root);
        _nodeCacheFlat = [ .._nodeCache.GetChildren() ];

        // Previously, context was initialized here. We could maybe move that elsewhere later.

        // Listen to changes from the draw system.
        DrawSystem.Changed += OnDrawSystemChange;
    }

    // Manages all nodes hover state over a 2 setters per drawframe outside updates.
    protected IDynamicNode? _hoveredNode = null; // From last frame.
    protected IDynamicNode? _newHoveredNode = null; // Tracked each frame.

    public virtual void Dispose()
    {
        DrawSystem.Changed -= OnDrawSystemChange;
    }

    public void DrawContents(float width, DynamicFlags flags = DynamicFlags.BasicViewFolder)
    {
        DrawContentsInternal(width, flags);
        PostDraw();
    }

    // Refactor later.
    private void DrawContentsInternal(float width, DynamicFlags flags)
    {
        using var _ = ImRaii.Child(Label, new Vector2(width, -1), false, WFlags.NoScrollbar);
        if (!_)
            return;
        HandleMainContextActions();
        Draw(flags);
    }

    private void Draw(DynamicFlags flags)
    {
        ImGui.SetScrollX(0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One)
            .Push(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale);

        // Apply the filter to generate the cache, if dirty.
        ApplyFilters();
        // We can update this as we develop further, fine tuning errors where we see them,
        // such as the above style interfering with drawn entities and such.
        DrawAll(flags);
    }

    /// <summary>
    ///     Add post-draw logic that is executed after drawing the full DynamicDrawSelector UI. <para />
    ///     Reserved for operations that would modify the cache state, or set the filter to dirty. <para />
    ///     If you override this make sure that the base is called, or else post-draw actions will not be processed.
    /// </summary>
    protected void PostDraw()
    {
        UpdateHoverNode();

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

    // Vastly change this overtime.
    private void OnDrawSystemChange(DDSChangeType type, IDynamicNode<T> obj, IDynamicCollection<T>? prevParent, IDynamicCollection<T>? newParent)
    {
        switch (type)
        {
            case DDSChangeType.ObjectMoved:
               // Enqueue the move operation as a post - draw action.
               _postDrawActions.Enqueue(() =>
               {
                   ExpandAncestors(obj);
                   MarkCacheDirty();
               });
               break;
            case DDSChangeType.ObjectRemoved:
            case DDSChangeType.Reload:
                if (obj == SelectedLeaf)
                    ClearSelected();
                MarkCacheDirty();
                break;
            default:
                MarkCacheDirty();
                break;
        }
    }
}
