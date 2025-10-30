using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     A DrawFolder that has implementable logic for regeneration, search filter updates, and reorders. <para />
///     Comes includes with <see cref="FolderOptions"/>, drag-drop support, and multi-selection support. <para />
///     Generated Dynamically as needed by updates, for DrawTime performance.
/// </summary>
public class DrawFolderGroup : DrawFolder
{
    private SundesmoGroup _group;
    public DrawFolderGroup(SundesmoGroup group, FolderOptions options, ILogger<DrawFolderGroup> log, 
        SundouleiaMediator mediator, MainConfig config, SharedFolderMemory memory,
        DrawEntityFactory factory, GroupsManager manager, SundesmoManager sundesmos)
        : base(group, options, log, mediator, config, memory, factory, manager, sundesmos)
    {
        _group = group;
        RegenerateItems(string.Empty);
        SetStylizations(group);
    }


    protected override IImmutableList<Sundesmo> GetAllItems()
        => _sundesmos.DirectPairs
            .Where(u => _group.LinkedUids.Contains(u.UserData.AliasOrUID))
            .ToImmutableList();

    protected override IEnumerable<FolderSortFilter> GetSortOrder()
        => _group.SortOrder;

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

    protected override void CleanupEntities(IEnumerable<Sundesmo> removedItems)
    {
        // Unsure what to do here yet but can find out.
    }

    // Can customize later.
    protected override void DrawOtherInfo()
    {
        CkGui.ColorTextFrameAlignedInline($"[{Online}]", ImGuiColors.DalamudGrey2);
        CkGui.AttachToolTip($"{Online} online\n{Total} total");
    }

    // Can polish later.
    protected override void DrawRightOptions()
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
    protected override void OnDragDropFinish(DrawFolder Source, DrawFolder Finish, List<Sundesmo> items)
    {
        Logger.LogDebug($"Drag-Drop finished from folder {Source.Label} to folder {Finish.Label} with {items.Count} items.");
        // If we are the source, we want to remove all items in the transfer from our group, and regenerate the list.
        if (Source.Label == Label)
        {
            Logger.LogDebug($"Removing {items.Count} items from group folder {_group.Label}.");
            // Remove all of the UID's from the groups linked UID's.
            if (_manager.UnlinkFromGroup(items.Select(u => u.UserData.UID), _group.Label))
                RegenerateItems(string.Empty);
        }
        // If we are the finish, we want to add all items in the transfer to our group, and regenerate the list.
        else if (Finish.Label == Label)
        {
            Logger.LogDebug($"Adding {items.Count} items to group folder {_group.Label}.");
            // Add all of the UID's into the groups linked UID's.
            if (_manager.LinkToGroup(items.Select(u => u.UserData.UID), _group.Label))
                RegenerateItems(string.Empty);
        }
    }
}
