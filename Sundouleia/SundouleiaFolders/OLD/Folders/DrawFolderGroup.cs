using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.Components;

/// <summary>
///     A DrawFolder that has implementable logic for regeneration, search filter updates, and reorders. <para />
///     Comes includes with <see cref="FolderOptions"/>, drag-drop support, and multi-selection support. <para />
///     Generated Dynamically as needed by updates, for DrawTime performance.
/// </summary>
public class DrawFolderGroup : DynamicPairFolder
{
    private SundesmoGroup _group;
    public DrawFolderGroup(SundesmoGroup group, FolderOptions options, ILogger<DrawFolderGroup> log,
        SundouleiaMediator mediator, FolderConfig config, SharedFolderMemory memory,
        DrawEntityFactory factory, GroupsManager groups, SundesmoManager sundesmos)
        : base(group.Label, options, log, mediator, config, factory, groups, memory, sundesmos)
    {
        _group = group;

        // Can regenerate the items here.
        RegenerateItems(string.Empty);
        // Set stylizations here.
        SetStylizations(group);
    }

    private void SetStylizations(SundesmoGroup group)
    {
        var oldLabel = Label;

        Icon = group.Icon;
        IconColor = group.IconColor;
        Label = group.Label;
        LabelColor = group.LabelColor;
        ColorBorder = group.BorderColor;
        ShowIfEmpty = group.ShowIfEmpty;
        ShowOffline = group.ShowOffline;

        // If the labels changed we need to regenerate all items.
        if (oldLabel != Label)
            RegenerateItems(string.Empty);
    }

    // Inheritance Satisfiers.
    protected override List<Sundesmo> GetAllItems()
        => _sundesmos.DirectPairs.Where(u => _group.LinkedUids.Contains(u.UserData.UID)).ToList();

    protected override IEnumerable<FolderSortFilter> GetSortOrder()
        => _group.SortOrder;

    protected override void DrawFolderInternal(bool toggles)
    {
        // pre-determine the size of the folder.
        var folderWidth = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : ColorBG;
        var rightWidth = CkGui.IconButtonSize(FAI.Cog).X + CkGui.IconButtonSize(FAI.Filter).X + ImUtf8.ItemInnerSpacing.X * 2f;
        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder_ {Label}", folderWidth, ImUtf8.FrameHeight, bgCol, ColorBorder, 5f, 1f))
        {
            var pos = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"folder_click_area_{Label}", new Vector2(folderWidth - rightWidth, _.InnerRegion.Y));

            // Back to start and then draw.
            ImGui.SameLine(pos.X);
            CkGui.FramedIconText(FAI.CaretDown);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(Icon, IconColor);
            CkGui.ColorTextFrameAlignedInline(Label, LabelColor);
            CkGui.ColorTextFrameAlignedInline($"[{Online}]", ImGuiColors.DalamudGrey2);
            CkGui.AttachToolTip($"{Online} online\n{Total} total");

            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth() - rightWidth);
            DrawRightOptions();
        }
        _hovered = ImGui.IsItemHovered();
    }

    // Steer away from popups, instead do expandable slide down view.
    private void DrawRightOptions()
    {
        var config = CkGui.IconButtonSize(FAI.Cog);
        var filter = CkGui.IconButtonSize(FAI.Filter);
        var spacingX = ImUtf8.ItemInnerSpacing.X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - config.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (CkGui.IconButton(FAI.Cog, inPopup: true))
            ImGui.OpenPopup("Folder Config Menu");
        CkGui.AttachToolTip("Open Folder Configuration");

        currentRightSide -= filter.X + spacingX;
        ImGui.SameLine(currentRightSide);
        if (CkGui.IconButton(FAI.Filter, inPopup: true))
            ImGui.OpenPopup("Folder Filter Menu");
        CkGui.AttachToolTip("Set Folder Filters");

        if (ImGui.BeginPopup("Folder Config Menu"))
        {
            DrawFolderConfigMenu(200f);
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopup("Folder Filter Menu"))
        {
            DrawFolderFilterMenu(200f);
            ImGui.EndPopup();
        }
    }

    private void DrawFolderConfigMenu(float width)
    {
        CkGui.ColorText("I'm a working FolderConfigMenu Popup!", ImGuiColors.DalamudRed);

    }

    private void DrawFolderFilterMenu(float width)
    {
        CkGui.ColorText("I'm a working FolderFilter Popup!", ImGuiColors.DalamudRed);
    }

    // Should probably publish this to a mediator so it can be called across folders
    // but then we would need disposable classes and it would make a mess. See about this more later.
    protected override void OnDragDropFinish(IDynamicFolder Source, IDynamicFolder Finish, List<IDrawEntity> transferred)
    {
        Logger.LogDebug($"Drag-Drop finished from folder {Source.Label} to folder {Finish.Label} with {transferred.Count} items.");
        // If we are the source, we want to remove all items in the transfer from our group, and regenerate the list.
        if (Source.Label == Label)
        {
            Logger.LogDebug($"Removing {transferred.Count} items from group folder {_group.Label}.");
            // Remove all of the UID's from the groups linked UID's.
            if (_groups.UnlinkFromGroup(transferred.Select(u => u.EntityId), _group.Label))
                RegenerateItems(string.Empty); // still figuring this one out.
        }
        // If we are the finish, we want to add all items in the transfer to our group, and regenerate the list.
        else if (Finish.Label == Label)
        {
            Logger.LogDebug($"Adding {transferred.Count} items to group folder {_group.Label}.");
            // Add all of the UID's into the groups linked UID's.
            if (_groups.LinkToGroup(transferred.Select(u => u.EntityId), _group.Label))
                RegenerateItems(string.Empty);
        }
    }
}
