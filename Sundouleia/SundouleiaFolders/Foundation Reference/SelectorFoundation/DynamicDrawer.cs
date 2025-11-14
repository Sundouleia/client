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

    // As we construct this further, pull more things into their own classes and add them here.

    protected readonly DynamicDrawSystem<T> DrawSystem;

    private readonly string _label = string.Empty;

    // Queue of all actions to perform after completely drawing the list,
    // to avoid processing operations that would modify the filter mid-draw. (Can be reworked later if we need to)
    private readonly Queue<Action> _postDrawActions = new();

    public DynamicDrawer(string label, ILogger log, DynamicDrawSystem<T> drawSystem)
    {
        DrawSystem = drawSystem;
        Log        = log;
        Label      = label;

        // Placeholder for state until it is moved to its own managed cache.
        _cachedState = new List<EntityState>();

        // Previously, context was initialized here. We could maybe move that elsewhere later.

        // Listen to changes from the draw system.
        DrawSystem.Changed += OnDrawSystemChange;
    }

    public string Label
    {
        get => _label;
        init
        {
            _label = value;
            // Drag-Drop labels should be set here, for wherever is necessary.
        }
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
        string tmp = FilterValue;

        if (FancySearchBar.Draw("Filter", width, ref tmp, string.Empty, 128, width - searchW, DrawCustomFilters))
        {
            if (!string.Equals(tmp, FilterValue, StringComparison.Ordinal))
                SetFilterDirty();
        }

        // could maybe add inline button context here but not really that sure. Think about it later.

        // Draw popup context here for some filter button interactions, but can handle it otherwise later if needed.
    }

    /// <summary> 
    ///     Draw the main content region for the DynamicDrawer. 
    /// </summary>
    public void DrawContents(float width)
    {
        using var _ = ImRaii.PushColor(ImGuiCol.ButtonHovered, 0).Push(ImGuiCol.ButtonActive, 0);

        DrawContentsInternal(width);
        PostDraw();
    }

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

    private void Draw()
    {
        ImGui.SetScrollX(0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale)
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(ImUtf8.ItemSpacing.X, ImGuiHelpers.GlobalScale))
            .Push(ImGuiStyleVar.FramePadding, new Vector2(ImGuiHelpers.GlobalScale, ImUtf8.FramePadding.Y));

        // Apply the filter to generate the cache, if dirty.
        ApplyFilters();
        // Jump to the filtered selection if requested.
        JumpIfRequested();

        // We can update this as we develop further, fine tuning errors where we see them,
        // such as the above style interfering with drawn entities and such.
        DrawEntities();
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

    // Quick-Access a leaf by its T Value, expanding, selecting, and jumping to it.
    public void SelectByValue(T data)
    {
        // Obtain the leaves associated with this data value.
        if (!DrawSystem.TryGetValue(data, out var leaves))
            return;

        _postDrawActions.Enqueue(() =>
        {
            foreach (var leaf in leaves)
            {
                _filterDirty |= ExpandAncestors(leaf);
                Select(leaf);
                _jumpToSelection = leaf;
            }
        });
    }

    // We could maybe handle this with overloads somewhere but idk. For now this works.
    private void OnDrawSystemChange(DDSChangeType type, DynamicDrawSystem<T>.IDynamicEntity obj, DynamicDrawSystem<T>.IDynamicFolder? prevParent, DynamicDrawSystem<T>.IDynamicFolder? newParent)
    {
        switch (type)
        {
            case DDSChangeType.ObjectMoved:
               // Enqueue the move operation as a post - draw action.
               _postDrawActions.Enqueue(() =>
               {
                   ExpandAncestors(obj);
                   SetFilterDirty();
               });
                break;
            case DDSChangeType.ObjectRemoved:
            case DDSChangeType.Reload:
                if (obj == SelectedLeaf)
                    ClearSelected();
                else if (AllowMultiSelection)
                {
                    // Remove Selection, set dirty (not sure why we should do a full reload here but whatever)
                    // Deselect(obj);
                    SetFilterDirty();
                }
                SetFilterDirty();
                break;
            default:
                SetFilterDirty();
                break;
        }
    }
}
