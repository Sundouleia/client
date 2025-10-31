using CkCommons;
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
///     Just a DrawFolder with some helpers to parse out name strings and such.
/// </summary>
public class DrawFolderDefault : DynamicPairFolder
{
    public DrawFolderDefault(string label, FolderOptions options, ILogger<DrawFolderDefault> log, 
        SundouleiaMediator mediator, MainConfig config, SharedFolderMemory memory, 
        DrawEntityFactory factory, GroupsManager groups, SundesmoManager sundesmos)
        : base(label, options, log, mediator, config, factory, groups, memory, sundesmos)
    {
        LabelColor = uint.MaxValue;
        ColorBG = uint.MinValue;
        ColorBorder = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        switch (label)
        {
            case Constants.FolderTagAllDragDrop:
            case Constants.FolderTagAll:
                Icon = FAI.Globe;
                IconColor = uint.MaxValue;
                break;
            case Constants.FolderTagVisible:
                Icon = FAI.Eye;
                IconColor = CkColor.TriStateCheck.Uint();
                break;
            case Constants.FolderTagOnline:
                Icon = FAI.Link;
                IconColor = CkColor.TriStateCheck.Uint();
                break;
            case Constants.FolderTagOffline:
                Icon = FAI.Link;
                IconColor = CkColor.TriStateCross.Uint();
                break;
        }
        // Set the render if empty variable.
        ShowIfEmpty = Label switch
        {
            Constants.FolderTagAll => true,
            Constants.FolderTagAllDragDrop => true,
            Constants.FolderTagVisible => false,
            Constants.FolderTagOnline => false,
            Constants.FolderTagOffline => false,
            _ => false,
        };

        // Can regenerate the items here.
        RegenerateItems(string.Empty);

        Mediator.Subscribe<RegenerateEntries>(this, _ =>
        {
            if (_.TargetFolders is RefreshTarget.Sundesmos)
                RegenerateItems(string.Empty);
        });
    }

    protected override void DrawFolderInternal()
    {
        // pre-determine the size of the folder.
        var folderWidth = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : ColorBG;
        // Draw framed child via CkRaii with background based on hover state 
        using (var _ = CkRaii.FramedChildPaddedW($"sundouleia_folder_ {Label}", folderWidth, ImUtf8.FrameHeight, bgCol, ColorBorder, 5f, 1f))
        {
            var pos = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"folder_click_area_{Label}", new Vector2(folderWidth, _.InnerRegion.Y));
            if (ImGui.IsItemClicked())
                _groups.ToggleState(Label);

            // Back to start and then draw.
            ImGui.SameLine(pos.X);
            CkGui.FramedIconText(_groups.IsOpen(Label) ? FAI.CaretDown : FAI.CaretRight);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(Icon, IconColor);
            CkGui.ColorTextFrameAlignedInline(Label, LabelColor);
            CkGui.ColorTextFrameAlignedInline(GetBracketText(), ImGuiColors.DalamudGrey2);
            CkGui.AttachToolTip(GetBracketTooltip());
        }
        _hovered = ImGui.IsItemHovered();
    }

    protected override List<Sundesmo> GetAllItems()
        => Label switch
        {
            Constants.FolderTagAll => _sundesmos.DirectPairs,
            Constants.FolderTagAllDragDrop => _sundesmos.DirectPairs,
            Constants.FolderTagVisible => _sundesmos.DirectPairs.Where(u => u.IsRendered && u.IsOnline).ToList(),
            Constants.FolderTagOnline => _sundesmos.DirectPairs.Where(u => u.IsOnline).ToList(),
            Constants.FolderTagOffline => _sundesmos.DirectPairs.Where(u => !u.IsOnline).ToList(),
            _ => new List<Sundesmo>(),
        };

    private string GetBracketText() => Label switch
    {
        Constants.FolderTagAll => $"[{Total}]",
        Constants.FolderTagVisible => $"[{Rendered}]",
        Constants.FolderTagOnline => $"[{Online}]",
        Constants.FolderTagOffline => $"[{Total}]",
        _ => string.Empty,
    };

    private string GetBracketTooltip() => Label switch
    {
        Constants.FolderTagAll => $"{Total} total",
        Constants.FolderTagVisible => $"{Online} visible",
        Constants.FolderTagOnline => $"{Online} online",
        Constants.FolderTagOffline => $"{Total} offline",
        _ => string.Empty,
    };

    // Should probably publish this to a mediator so it can be called across folders
    // but then we would need disposable classes and it would make a mess. See about this more later.
    protected override void OnDragDropFinish(IDynamicFolder Source, IDynamicFolder Finish, List<IDrawEntity> transferred)
    {
        // If we are the source, do nothing, we dont want to remove items from the _allSundesmos list.
        if (Source.Label == Label)
        {
            Logger.LogInformation($"Moved {transferred.Count} from this folder ({Label})");
        }
        // if we were the target, we also dont want to do anything, but still notify (for debug reasons).
        else if (Finish.Label == Label)
        {
            Logger.LogInformation($"Received {transferred.Count} into this folder ({Label})");
        }
    }
}
