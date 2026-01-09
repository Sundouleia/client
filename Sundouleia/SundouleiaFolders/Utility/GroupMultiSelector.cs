using CkCommons.DrawSystem;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Pairs;

namespace Sundouleia.DrawSystem;

// TODO:
// Revise this to work instead inside of a draw system dependancy.
// We should only do this because for pairs when we want to add or remove them from groups,
// we will need to track which folders should be visible, or which folders are applied.
public class RequestsGroupSelector : GroupMultiSelector
{
    public RequestsGroupSelector(ILogger logger, GroupsDrawSystem ds)
        : base(logger, ds)
    {
        // Bagagwa
    }

    public void DrawSelectorCombo(string id, string dispTxt, float width, float innerWidth, CFlags flags = CFlags.None)
    {
        using var _ = ImRaii.PushId(id);
        ImGui.SetNextItemWidth(width);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ScrollbarSize, 10f);

        using var combo = ImUtf8.Combo(""u8, dispTxt, CFlags.HeightLargest);
        if (!combo) return;
        // Inside of the combo, draw the interactions.
        ImGui.Dummy(new(innerWidth, 0f));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        DrawInternal(id, innerWidth);
    }

    public void DrawSelectedList(string id, float width)
    {
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);
        foreach (var folder in Selected.ToList())
            DrawSelectedFolder(folder, width);
    }

    private void DrawSelectedFolder(IDynamicFolder<Sundesmo> folder, float width)
    {
        using var _ = CkRaii.FramedChildPaddedW(folder.ID.ToString(), width, ImUtf8.FrameHeight, folder.BgColor, folder.BorderColor, 5f, 1f);
        
        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(folder.Icon, folder.IconColor);
        CkGui.ColorTextFrameAlignedInline(folder.Name, folder.NameColor);

        ImGui.SameLine(_.InnerRegion.X - CkGui.IconButtonSize(FAI.Minus).X);
        if (CkGui.IconButton(FAI.Minus, id: $"dsl_{folder.ID}", inPopup: true))
            DeselectFolder(folder);
        CkGui.AttachToolTip($"Remove from Selection");
    }

    // No need to override this for the requests as we can just pull them in bulk after.
    protected override void SelectFolder(IDynamicFolder<Sundesmo> folder)
    {
        base.SelectFolder(folder);
    }

    // No need to override this for the requests as we can just pull them in bulk after.
    protected override void DeselectFolder(IDynamicFolder<Sundesmo> folder)
    {
        base.DeselectFolder(folder);
    }
}

/// <summary>
///     For selecting desired group and FolderGroups with checkboxes. <para />
///     Other elements can override what occurs on selection / deselection.
/// </summary>
public abstract class GroupMultiSelector
{
    protected readonly ILogger _logger;
    protected readonly GroupsDrawSystem _drawSystem;

    // The Selected Folders.
    protected HashSet<IDynamicFolder<Sundesmo>> Selected = new();

    protected GroupMultiSelector(ILogger logger, GroupsDrawSystem ds)
    {
        _logger = logger;
        _drawSystem = ds;
    }

    // Maybe revise, idfk.
    protected void DrawInternal(string id, float width, float height = -1)
    {
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);
        var rootFolder = _drawSystem.Root;
        foreach (var node in rootFolder.Children)
            DrawCollection(node);
    }

    protected virtual void DrawCollection(IDynamicCollection<Sundesmo> collection)
    {
        if (collection is IDynamicFolderGroup<Sundesmo> group)
            DrawFolderGroup(group);
        else if (collection is GroupFolder folder)
            DrawFolder(folder);
    }

    protected virtual void DrawFolderGroup(IDynamicFolderGroup<Sundesmo> folderGroup)
    {
        var flags = folderGroup.IsOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        using var node = ImUtf8.TreeNode(folderGroup.Name, flags);
        if (!node) return;

        foreach (var child in folderGroup.Children)
            DrawCollection(child);
    }

    protected virtual void DrawFolder(GroupFolder folder)
    {
        var contains = Selected.Contains(folder);
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        using (CkRaii.FramedChildPaddedW(folder.ID.ToString(), width, ImUtf8.FrameHeight, folder.BgColor, folder.BorderColor, 5f, 1f))
        {
            if (ImGui.Checkbox($"##{folder.ID}", ref contains))
            {
                if (contains) SelectFolder(folder);
                else DeselectFolder(folder);
            }
            ImUtf8.SameLineInner();
            CkGui.IconTextAligned(folder.Icon, folder.IconColor);
            CkGui.ColorTextFrameAlignedInline(folder.Name, folder.NameColor);
        }
    }

    protected virtual void SelectFolder(IDynamicFolder<Sundesmo> folder)
    {
        Selected.Add(folder);
        _logger.LogInformation($"Selected folder: {folder.Name} ({folder.ID})");
    }

    protected virtual void DeselectFolder(IDynamicFolder<Sundesmo> folder)
    {
        Selected.Remove(folder);
        _logger.LogInformation($"Deselected folder: {folder.Name} ({folder.ID})");
    }
}
