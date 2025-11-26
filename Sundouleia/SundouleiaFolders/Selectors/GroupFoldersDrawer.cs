using CkCommons;
using CkCommons.Classes;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;

namespace Sundouleia.DrawSystem;

/// <summary>
///     A drawer for Groups that only displays folders. Used for managing Sundouleia's Group System. <para />
///     This references the same DrawSystem as the <see cref="GroupsDrawer"/> but only displays folders <para />
///     Also using this to draw group creators and deletions
/// </summary>
public class GroupsFolderDrawer : DynamicDrawer<Sundesmo>
{
    private static readonly IconCheckboxEx CheckboxOffline = new(FAI.Unlink);
    private static readonly IconCheckboxEx CheckboxShowEmpty = new(FAI.FolderOpen);
    // Used when arranging Groups.
    private static readonly string Tooltip =
        "--COL--[DRAG]:--COL-- Move the folder around, re-ordering it.--NL--" +
        "--COL--[L-CLICK]:--COL-- Add / Remove DragDrop selection.--NL--" +
        "--COL--[SHIFT + L-CLICK]: --COL-- Bulk Select/Deselect between last & current.";

    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly FolderConfig _folderConfig;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly GroupsDrawSystem _drawSystem;

    // Widgets
    private FAIconCombo       _iconSelector;

    // Track which folder has its config open.
    private bool _addingFolderGroup = false;
    private SundesmoGroup? _creating = null;
    private string _newGroupName = string.Empty;

    private string _nameEditTmp = string.Empty;
    private IDynamicCollection<Sundesmo>? _folderInEditor;

    public GroupsFolderDrawer(ILogger<GroupsDrawer> logger, SundouleiaMediator mediator, MainConfig config, 
        FolderConfig folderConfig, GroupsManager groups, SundesmoManager sundesmos, GroupsDrawSystem ds)
        : base("##GroupsFolderDrawer", logger, ds, new SundesmoCache(ds))
    {
        _mediator = mediator;
        _config = config;
        _folderConfig = folderConfig;
        _groups = groups;
        _sundesmos = sundesmos;
        _drawSystem = ds;

        _iconSelector = new FAIconCombo(logger);

        Cache.MarkCacheDirty();
    }

    #region OrganizerHelpers
    // Special method to draw a button row for creating / merging / deleting selected groups.
    public void DrawButtonHeader(float width)
    {
        var frameBg = ImGui.GetColorU32(ImGuiCol.FrameBg);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f);
        using var c = ImRaii.PushColor(ImGuiCol.FrameBg, 0);
        using var _ = ImRaii.Group();
        
        DrawButtonRow(width);
        // If we are creating a group, or a FolderGroup, draw the input text field.
        if (_creating is null && _addingFolderGroup is false)
            return;

        // Get the field and tooltip.
        var hint = _addingFolderGroup ? "Folder Group Name.." : "New Group Name..";
        // Add a Dummy spanning the width,
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(pos, pos + new Vector2(width, ImUtf8.FrameHeight), frameBg, 6f);
        var stopAdd = string.IsNullOrWhiteSpace(_newGroupName) || _drawSystem.FolderMap.ContainsKey(_newGroupName);
        if (CkGui.IconButton(FAI.FolderPlus, disabled: stopAdd, inPopup: true))
        {
            if (_addingFolderGroup)
            {
                Log.LogDebug($"Attempting to add new Folder Group: [{_newGroupName}]");
                if (_drawSystem.TryAddFolderGroup(_newGroupName))
                {
                    Log.LogInformation($"Created FolderGroup [{_newGroupName}] in DDS ({Label})");
                    // Reset the new group name, but keep us in adding folder groups if we ever wanted to add more.
                    _newGroupName = string.Empty;
                }
            }
            else if (_creating is not null)
            {
                Log.LogDebug("Attempting to add new Sundesmo Group.");
                _creating.Label = _newGroupName;
                if (_groups.TryAddNewGroup(_creating) && _drawSystem.TryAddGroup(_creating))
                {
                    Log.LogInformation($"Created SundesmoGroup [{_creating.Label}] in DDS ({Label})");
                    _creating = new SundesmoGroup();
                    _newGroupName = string.Empty;
                }
            }
        }
        ImGui.SameLine(0, 0);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##NewCollectionName", hint, ref _newGroupName, 40);
        CkGui.AttachToolTip("Add this node with the plus button." +
            "--SEP----COL--[R-CLICK]:--COL-- Cancels the creation process.", ImGuiColors.DalamudOrange);
        // If right clicked, cancel creation process.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            _addingFolderGroup = false;
            _creating = null;
            _newGroupName = string.Empty;
        }
    }

    private void DrawButtonRow(float width)
    {
        var bWidth = (width - ImUtf8.ItemInnerSpacing.X * 2) / 3;
        bool noCreate = Selector.Selected.Count is not 0;
        bool noDelete = Selector.Collections.Count is 0 || !ImGui.GetIO().KeyShift;

        if (CkGui.FancyButton(FAI.FolderPlus, "Group", bWidth, noCreate))
        {
            _addingFolderGroup = false;
            _creating = new SundesmoGroup();
            _newGroupName = string.Empty;
        }
        CkGui.AttachToolTip("Add a new Group!");

        ImUtf8.SameLineInner();
        if (CkGui.FancyButton(FAI.FolderTree, "Folder", bWidth, noCreate))
        {
            _addingFolderGroup = true;
            _creating = null;
            _newGroupName = string.Empty;
        }
        CkGui.AttachToolTip("Add a nestable Folder to organize your pair Groups.");

        ImUtf8.SameLineInner();
        if (CkGui.FancyButton(FAI.TrashAlt, "Delete", bWidth, noDelete))
        {
            Log.LogInformation("Deleting selected groups.");
            // Perform the delete operation.
        }
        CkGui.AttachToolTip("Delete ALL selected groups.--NL--" +
            "--COL--Must be holding shift to delete.--COL--", ImGuiColors.DalamudOrange);
    }
    #endregion OrganizerHelpers

    protected override void HandleInteraction(IDynamicCollection<Sundesmo> folder, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _newHoveredNode = folder;

        // Handle Selection. (Maybe change it so it requires a ctrl click or shift idfk. Adding a flag for clickSelect could work.)
        if (flags.HasAny(DynamicFlags.SelectableFolders) && ImGui.IsItemClicked())
            Selector.SelectItem(folder, flags.HasFlag(DynamicFlags.MultiSelect), flags.HasFlag(DynamicFlags.RangeSelect));

        // Handle Drag. Drop handled externally.
        if (flags.HasAny(DynamicFlags.DragDropFolders))
            AsDragDropSource(folder);
    }

    protected override void PostDragSourceText(IDynamicNode<Sundesmo> entity)
    {
        if (entity is not IDynamicCollection<Sundesmo> || !IsDragging || _hoveredNode == entity)
            return;

        CkGui.Separator(uint.MaxValue);
        var shiftHeld = ImGui.GetIO().KeyShift;
        string message = DragDropCache switch
        {
            // CASE 1: Only collections
            { OnlyCollections: true, OnlyFolders: false, OnlyGroups: false } => 
                $"Dropping collection into [{entity.Name}]",

            // CASE 2: Target is FolderGroup
            { OnlyFolders: true } when entity is IDynamicFolderGroup<Sundesmo> fg =>
                $"Dropping folders into: {fg.Name}",

            { OnlyCollections: true } when entity is IDynamicFolderGroup<Sundesmo> fg =>
                shiftHeld
                    ? $"Merging folders into: {fg.Name}"
                    : $"Dropping folders into: {fg.Parent.Name}",

            // CASE 3: Target is Folder
            { OnlyFolders: true } when entity is IDynamicFolder<Sundesmo> f =>
                shiftHeld
                    ? $"Merging all pairs from selected into: {f.Name}"
                    : $"Dropping all Groups into: {f.Parent.Name}",

            { OnlyCollections: true } when entity is IDynamicFolder<Sundesmo> f =>
                $"Dropping groups into: {f.Name}",

            _ => string.Empty
        };
        CkGui.ColorTextFrameAligned(message, ImGuiColors.DalamudYellow);
    }


    protected override void DrawFolderBanner(IDynamicFolder<Sundesmo> f, DynamicFlags flags, bool selected)
    {
        // Ensure we draw the base for the all folder.
        if (f is not GroupFolder gf)
        {
            base.DrawFolderBanner(f, flags, selected);
            return;
        }
        // Otherwise draw the group folder.
        var editing = _folderInEditor == f;
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        // If we are editing, draw both, otherwise, draw only the header.
        if (!editing)
            DrawFolderRow(gf, width, flags, selected);
        else
        {
            using (ImRaii.Group())
            {
                DrawFolderRowEditing(gf, width, flags, selected);
                DrawFolderEditor(gf, width, flags, editing);
            }
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
        }
    }

    private void DrawFolderRow(GroupFolder folder, float width, DynamicFlags flags, bool selected)
    {
        // We could likely reduce this by a lot if we had a override for this clipped draw within the dynamic draw system.
        var rWidth = CkGui.IconButtonSize(FAI.Cog).X;
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : folder.BgColor;
        // Display a framed child with stylizations based on the folders preferences.
        using var _ = CkRaii.FramedChildPaddedW(Label + folder.ID, width, ImUtf8.FrameHeight, bgCol, folder.BorderColor, 5f, 1f);

        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton(Label + folder.ID, new(width - rWidth, ImUtf8.FrameHeight));
        HandleInteraction(folder, flags);
        CkGui.AttachToolTip(Tooltip, IsDragging || !flags.HasAny(DynamicFlags.DragDropFolders), ImGuiColors.DalamudOrange);

        // Draw the grip lines, folder icon, and name.
        ImGui.SameLine(pos.X);
        CkGui.FramedIconText(FAI.GripLines, folder.BorderColor);
        ImUtf8.SameLineInner();
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(folder.Icon, folder.IconColor);
        CkGui.ColorTextFrameAlignedInline(folder.Name, folder.NameColor);
        // Then the cog on the right.
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth() - rWidth);
        DrawFolderOptions(folder);
    }

    private void DrawFolderRowEditing(GroupFolder folder, float width, DynamicFlags flags, bool selected)
    {
        var rWidth = CkGui.IconButtonSize(FAI.Cog).X;
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : folder.BgColor;
        // Display a framed child with stylizations based on the folders preferences.
        using var _ = CkRaii.FramedChildPaddedW(Label + folder.ID, width, ImUtf8.FrameHeight, bgCol, folder.BorderColor, 5f, 1f);

        ImUtf8.SameLineInner();
        CkGui.FramedIconText(FAI.GripLines);
        ImUtf8.SameLineInner();
        // Display the editor version of this groups icon, allowing it to be changed.
        if (_iconSelector.Draw("IconSel", folder.Icon, 10))
        {
            // Update the icon within the group manager.
            if (_groups.TrySetIcon(folder.Name, _iconSelector.Current, folder.IconColor))
                folder.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Edit the icon for your group.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth((ImGui.GetContentRegionAvail().X - rWidth) / 2);
        var nameTmp = folder.Name;
        ImGui.InputTextWithHint("##GroupNameEdit", "Set Name..", ref _nameEditTmp, 40);
        if (ImGui.IsItemDeactivatedAfterEdit() && _groups.TryRename(folder.Name, _nameEditTmp))
        {
            DrawSystem.Rename(folder, _nameEditTmp);
            Cache.MarkForReload(folder.Parent);
        }
        CkGui.AttachToolTip("The name of this group.");

        ImGui.SameLine();
        DrawFolderOptions(folder);
    }

    private void DrawFolderEditor(GroupFolder f, float width, DynamicFlags flags, bool editing)
    {
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(8, 0))
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(4f));
        using var child = CkRaii.ChildPaddedW("FolderEditView", width, CkStyle.TwoRowHeight(), bgCol, 5f);
        using var _ = ImRaii.Table("FolderEditTable", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV);

        if (!_)
            return;

        ImGui.TableSetupColumn("Colors");
        ImGui.TableSetupColumn("Flags");
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var iconCol = ImGui.ColorConvertU32ToFloat4(f.IconColor);
        if (ImGui.ColorEdit4("Icon", ref iconCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {

            if (_groups.TrySetStyle(f.Name, ImGui.ColorConvertFloat4ToU32(iconCol), f.NameColor, f.BorderColor, f.GradientColor))
                f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Change the color of the folder icon.");


        var labelCol = ImGui.ColorConvertU32ToFloat4(f.NameColor);
        if (ImGui.ColorEdit4("Label", ref labelCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            if (_groups.TrySetStyle(f.Name, f.IconColor, ImGui.ColorConvertFloat4ToU32(labelCol), f.BorderColor, f.GradientColor))
                f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Change the color of the folder label.");

        // Other two colors.
        ImGui.TableNextColumn();
        var borderCol = ImGui.ColorConvertU32ToFloat4(f.BorderColor);
        if (ImGui.ColorEdit4("Border", ref borderCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            if (_groups.TrySetStyle(f.Name, f.IconColor, f.NameColor, ImGui.ColorConvertFloat4ToU32(borderCol), f.GradientColor))
                f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Change the color of the folder border.");

        var gradCol = ImGui.ColorConvertU32ToFloat4(f.GradientColor);
        if (ImGui.ColorEdit4("Gradient", ref gradCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs))
        {
            if (_groups.TrySetStyle(f.Name, f.IconColor, f.NameColor, f.BorderColor, ImGui.ColorConvertFloat4ToU32(gradCol)))
                f.ApplyLatestStyle();
        }
        CkGui.AttachToolTip("Change the color of the folder gradient.");

        ImGui.TableNextColumn();
        var showOffline = f.ShowOffline;
        if (CheckboxOffline.Draw("Show Offline"u8, ref showOffline))
        {
            if (_groups.TrySetState(f.Name, showOffline, f.ShowIfEmpty))
            {
                // Update the folder within the file system and mark things for a reload.
                DrawSystem.UpdateFolder(f);
                Cache.MarkForReload(f);
            }
        }
        CkGui.AttachToolTip("Show offline pairs in this folder.");

        var showIfEmpty = f.Flags.HasAny(FolderFlags.ShowIfEmpty);
        if (CheckboxShowEmpty.Draw("Show Empty"u8, ref showIfEmpty))
        {
            f.SetShowEmpty(showIfEmpty);
            if (_groups.TrySetState(f.Name, f.ShowOffline, f.ShowIfEmpty))
                Cache.MarkForReload(f);
        }
        CkGui.AttachToolTip("Folder is shown even with 0 items are filtered");
    }

    private float DrawFolderOptions(GroupFolder folder)
    {
        var isFolderInEditor = _folderInEditor == folder;
        var config = CkGui.IconButtonSize(FAI.Cog);
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - config.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (CkGui.IconButton(FAI.Cog, inPopup: !isFolderInEditor))
            ToggleEditor(folder);
        CkGui.AttachToolTip("Edit Group");
        return currentRightSide;
    }

    private void ToggleEditor(GroupFolder folder)
    {
        if (_folderInEditor == folder)
        {
            _folderInEditor = null;
            _nameEditTmp = string.Empty;
        }
        else
        {
            _folderInEditor = folder;
            _nameEditTmp = folder.Name;
        }
    }
}
