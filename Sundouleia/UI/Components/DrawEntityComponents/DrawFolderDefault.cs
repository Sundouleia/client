using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     Just a DrawFolder with some helpers to parse out name strings and such.
/// </summary>
public class DrawFolderDefault : DrawFolder
{
    public DrawFolderDefault(string label, FolderOptions options, ILogger<DrawFolderDefault> log, SundouleiaMediator mediator,
        MainConfig config, SharedFolderMemory memory, DrawEntityFactory factory, GroupsManager groups,
        SundesmoManager sundesmos)
        : base(label, options, log, mediator, config, memory, factory, groups, sundesmos)
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
    }

    protected override void DrawOtherInfo()
    {
        CkGui.ColorTextFrameAlignedInline(GetBracketText(), ImGuiColors.DalamudGrey2);
        CkGui.AttachToolTip(GetBracketTooltip());
    }

    protected override IImmutableList<Sundesmo> GetAllItems()
        => Label switch
        {
            Constants.FolderTagAll => _sundesmos.DirectPairs.ToImmutableList(),
            Constants.FolderTagAllDragDrop => _sundesmos.DirectPairs.ToImmutableList(),
            Constants.FolderTagVisible => _sundesmos.DirectPairs.Where(u => u.IsRendered && u.IsOnline).ToImmutableList(),
            Constants.FolderTagOnline => _sundesmos.DirectPairs.Where(u => u.IsOnline).ToImmutableList(),
            Constants.FolderTagOffline => _sundesmos.DirectPairs.Where(u => !u.IsOnline).ToImmutableList(),
            _ => ImmutableList<Sundesmo>.Empty,
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
    protected override void OnDragDropFinish(DrawFolder Source, DrawFolder Finish, List<Sundesmo> items)
    {
        // If we are the source, do nothing, we dont want to remove items from the _allSundesmos list.
        if (Source.Label == Label)
        {
            Logger.LogInformation($"Moved {items.Count} from this folder ({Label})");
        }
        // if we were the target, we also dont want to do anything, but still notify (for debug reasons).
        else if (Finish.Label == Label)
        {
            Logger.LogInformation($"Received {items.Count} into this folder ({Label})");
        }
    }
}
