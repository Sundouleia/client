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

    public virtual void Dispose()
    {
        DrawSystem.Changed -= OnDrawSystemChange;
    }

    // Rework these... heavily. Lol, Total visual overhaul required.
    protected virtual float FilterButtonsWidth(float width) => width;
    protected virtual void DrawCustomFilters()
    { }

    /// <summary>
    ///     Overridable DynamicDrawer 'Header' Element. (Filter Search) <para />
    ///     Not strictly required for a DynamicDrawer, but modifies what elements are displayed.
    /// </summary>
    public virtual void DrawFilter(float width)
    {
        using ImRaii.IEndObject group = ImRaii.Group();
        float searchW = FilterButtonsWidth(width);
        string tmp = Filter;

        if (FancySearchBar.Draw("Filter", width, ref tmp, string.Empty, 128, width - searchW, DrawCustomFilters))
        {
            if (!string.Equals(tmp, Filter, StringComparison.Ordinal))
                MarkCacheDirty();
        }

        // could maybe add inline button context here but not really that sure. Think about it later.

        // Draw popup context here for some filter button interactions, but can handle it otherwise later if needed.
    }

    public void DrawContents(float width)
    {
        DrawContentsInternal(width);
        PostDraw();
    }

    // Refactor later.
    private void DrawContentsInternal(float width)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        using var _ = ImRaii.Child(Label, new Vector2(width, -1), false, WFlags.NoScrollbar);
        // Make padding normal after drawing the child.
        style.Pop();
        // Avoid drawing if there is no content.
        if (!_)
            return;

        HandleMainContextActions();
        Draw();
    }

    private void Draw(DynamicFlags flags = DynamicFlags.BasicViewFolder)
    {
        ImGui.SetScrollX(0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(ImUtf8.ItemSpacing.X, ImGuiHelpers.GlobalScale))
            .Push(ImGuiStyleVar.FramePadding, new Vector2(ImGuiHelpers.GlobalScale, ImUtf8.FramePadding.Y));

        // Apply the filter to generate the cache, if dirty.
        ApplyFilters();
        // We can update this as we develop further, fine tuning errors where we see them,
        // such as the above style interfering with drawn entities and such.
        DrawAll(flags);
    }

    /// <summary>
    ///     Add post-draw logic that is executed after drawing the full DynamicDrawSelector UI. <para />
    ///     Reserved for operations that would modify the cache state, or set the filter to dirty.
    /// </summary>
    private void PostDraw()
    {
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
