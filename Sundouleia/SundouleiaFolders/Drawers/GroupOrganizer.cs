//using CkCommons;
//using CkCommons.Classes;
//using CkCommons.DrawSystem;
//using CkCommons.DrawSystem.Selector;
//using CkCommons.Gui;
//using CkCommons.Gui.Utility;
//using CkCommons.Raii;
//using Dalamud.Bindings.ImGui;
//using Dalamud.Interface;
//using Dalamud.Interface.Colors;
//using Dalamud.Interface.Utility.Raii;
//using OtterGui.Text;
//using Sundouleia.CustomCombos;
//using Sundouleia.Pairs;
//using Sundouleia.PlayerClient;
//using Sundouleia.Services;
//using Sundouleia.Services.Mediator;

//namespace Sundouleia.DrawSystem;

///// <summary>
/////     A drawer for Groups that only displays folders. Used for managing Sundouleia's Group System. <para />
/////     This references the same DrawSystem as the <see cref="GroupsDrawer"/> but only displays folders <para />
/////     Also using this to draw group creators and deletions
///// </summary>
//public class GroupOrganizer : DynamicDrawer<Sundesmo>
//{
//    private static readonly IconCheckboxEx CheckboxOffline = new(FAI.Unlink);
//    private static readonly IconCheckboxEx CheckboxPin = new(FAI.MapPin);
//    // Used when arranging Groups.
//    private static readonly string Tooltip =
//        "--COL--[DRAG]:--COL-- Move the folder around, re-ordering it.--NL--" +
//        "--COL--[L-CLICK]:--COL-- Add / Remove DragDrop selection.--NL--" +
//        "--COL--[SHIFT + L-CLICK]: --COL-- Bulk Select/Deselect between last & current.";

//    private readonly SundouleiaMediator _mediator;
//    private readonly MainConfig _config;
//    private readonly FolderConfig _folderConfig;
//    private readonly GroupsManager _groups;
//    private readonly SundesmoManager _sundesmos;
//    private readonly GroupsDrawSystem _drawSystem;

//    // Widgets
//    private DataCenterCombo _dcCombo;
//    private WorldCombo      _worldCombo;
//    private TerritoryCombo  _territoryCombo;

//    private FAIconCombo     _iconSelector;

//    // Track which folder has its config open.

//    private string _nameEditTmp = string.Empty;
//    private IDynamicCollection<Sundesmo>? _folderInEditor;

//    public GroupOrganizer(ILogger<GroupOrganizer> logger, SundouleiaMediator mediator, 
//        MainConfig config, FolderConfig folders, GroupsManager groups, SundesmoManager sundesmos, 
//        GroupsDrawSystem ds)
//        : base("##GroupsFolderDrawer", Svc.Logger.Logger, ds, new WhitelistCache(ds))
//    {
//        _mediator = mediator;
//        _config = config;
//        _folderConfig = folders;
//        _groups = groups;
//        _sundesmos = sundesmos;
//        _drawSystem = ds;

//        _iconSelector = new FAIconCombo(logger);

//        _dcCombo = new DataCenterCombo(logger);
//        _worldCombo = new WorldCombo(logger);
//        _territoryCombo = new TerritoryCombo(logger);

//        FilterCache.MarkCacheDirty();
//    }

//    // Could make this pass in if the button was clicked.
//    // Alternatively we could just override the whole thing with additional methods for the folderGroup and folder.
//    protected override void HandleLeftClick(IDynamicCollection<Sundesmo> folder, DynamicFlags flags)
//    {
//        bool isGroup = folder is DynamicFolderGroup<Sundesmo>;
//        bool canSelect = flags.HasAny(DynamicFlags.SelectableFolders);
//        bool ctrlPressed = ImGui.GetIO().KeyCtrl;

//        if (isGroup && !ctrlPressed)
//            DrawSystem.SetOpenState(folder, !folder.IsOpen);

//        if (canSelect && (!isGroup || ctrlPressed))
//            Selector.SelectItem(folder, flags.HasFlag(DynamicFlags.MultiSelect), flags.HasFlag(DynamicFlags.RangeSelect));
//    }

//    protected override void HandleDetections(IDynamicCollection<Sundesmo> node, DynamicFlags flags)
//    {
//        if (ImGui.IsItemHovered())
//            _newHoveredNode = node;

//        // Handle Drag, return early if dragging.
//        // Drop handled externally.
//        if (flags.HasAny(DynamicFlags.DragDropFolders))
//            AsDragDropSource(node);
//    }

//    private void DrawFolderRow(GroupFolder folder, float width, DynamicFlags flags, bool selected)
//    {
//        // We could likely reduce this by a lot if we had a override for this clipped draw within the dynamic draw system.
//        var rWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconButtonSize(FAI.MapPin).X;
//        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : folder.BgColor;
//        // Display a framed child with stylizations based on the folders preferences.
//        using var _ = CkRaii.FramedChildPaddedW(Label + folder.ID, width, ImUtf8.FrameHeight, bgCol, folder.BorderColor, 5f, 1f);

//        var pos = ImGui.GetCursorPos();
//        if (ImGui.InvisibleButton(Label + folder.ID, new(width - rWidth, ImUtf8.FrameHeight)))
//            HandleLeftClick(folder, flags);
//        HandleDetections(folder, flags);

//        CkGui.AttachToolTip(Tooltip, IsDragging || !flags.HasAny(DynamicFlags.DragDropFolders), ImGuiColors.DalamudOrange);

//        // Draw the grip lines, folder icon, and name.
//        ImGui.SameLine(pos.X);
//        CkGui.FramedIconText(FAI.GripLines, folder.BorderColor);
//        ImUtf8.SameLineInner();
//        ImGui.AlignTextToFramePadding();
//        CkGui.IconText(folder.Icon, folder.IconColor);
//        CkGui.ColorTextFrameAlignedInline(folder.Name, folder.NameColor);
//        // Then the cog on the right.
//        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth() - rWidth);
//        DrawFolderOptions(folder);
//    }

//    private void DrawFolderRowEditing(GroupFolder folder, float width, DynamicFlags flags, bool selected)
//    {
//        var rWidth = CkGui.IconButtonSize(FAI.Cog).X;
//        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : folder.BgColor;
//        // Display a framed child with stylizations based on the folders preferences.
//        using var _ = CkRaii.FramedChildPaddedW(Label + folder.ID, width, ImUtf8.FrameHeight, bgCol, folder.BorderColor, 5f, 1f);

//        ImUtf8.SameLineInner();
//        CkGui.FramedIconText(FAI.GripLines);
//        ImUtf8.SameLineInner();
//        // Display the editor version of this groups icon, allowing it to be changed.
//        if (_iconSelector.Draw("IconSel", folder.Icon, 10))
//        {
//            // Update the icon within the group manager.
//            _groups.SetIcon(folder.Group, _iconSelector.Current, folder.IconColor);
//            folder.ApplyLatestStyle();
//        }
//        CkGui.AttachToolTip("Edit the icon for your group.");

//        ImGui.SameLine();
//        ImGui.SetNextItemWidth((ImGui.GetContentRegionAvail().X - rWidth) / 2);
//        var nameTmp = folder.Name;
//        ImGui.InputTextWithHint("##GroupNameEdit", "Set Name..", ref _nameEditTmp, 40);
//        if (ImGui.IsItemDeactivatedAfterEdit() && _groups.TryRename(folder.Group, _nameEditTmp))
//        {
//            DrawSystem.Rename(folder, _nameEditTmp);
//            FilterCache.MarkForReload(folder.Parent);
//        }
//        CkGui.AttachToolTip("The name of this group.");

//        ImGui.SameLine();
//        DrawFolderOptions(folder);
//    }


//    private float DrawFolderOptions(GroupFolder folder)
//    {
//        var isFolderInEditor = _folderInEditor == folder;
//        var endX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();

//        endX -= CkGui.IconButtonSize(FAI.Cog).X;
//        ImGui.SameLine(endX);
//        if (CkGui.IconButton(FAI.Cog, inPopup: !isFolderInEditor))
//            ToggleEditor(folder);
//        CkGui.AttachToolTip("Edit Group");

//        if (folder.Group.AreaBound)
//        {
//            endX -= CkGui.IconButtonSize(FAI.Map).X;
//            ImGui.SameLine(endX);
//            if (CkGui.IconButton(FAI.Map, inPopup: !isFolderInEditor))
//                ToggleLocationEditor(folder);
//            CkGui.AttachToolTip("Edit Attached Location");
//        }

//        return endX;
//    }

//}
