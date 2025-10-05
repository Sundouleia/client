using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using System.Collections.Immutable;

namespace Sundouleia.Gui.MainWindow;

// This is a placeholder UI structure for the sake of getting testing
// functional, and will be restructured later.
public class RequestsTab : DisposableMediatorSubscriberBase
{
    private const string INCOMING_ID = "Incoming Requests";
    private const string OUTGOING_ID = "Sent Requests";

    private readonly MainHub _hub;
    private readonly GroupsConfig _config;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;

    private bool _hoveringIncoming = false;
    private bool _hoveringOutgoing = false;
    private ImmutableList<DrawSundesmoRequest> _incoming;
    private ImmutableList<DrawSundesmoRequest> _outgoing;

    public RequestsTab(ILogger<RequestsTab> logger, SundouleiaMediator mediator,
        MainHub hub, GroupsConfig config, SundesmoManager sundesmos, RequestsManager requests) 
        : base(logger, mediator)
    {
        _hub = hub;
        _config = config;
        _sundesmos = sundesmos;
        _requests = requests;

        RecreateRequests();

        Mediator.Subscribe<RefreshRequestsMessage>(this, _ => RecreateRequests());
    }

    public void DrawRequestsSection()
    {
        using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        
        DrawIncoming();
        DrawOutgoing();
    }

    // Picks between incoming and outgoing requests, so we know what to draw and such.
    private void DrawIncoming()
    {
        var expanded = _config.IsDefaultExpanded(INCOMING_ID);

        using var id = ImRaii.PushId("folder_" + INCOMING_ID);
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());

        using (CkRaii.Child($"folder__{INCOMING_ID}", childSize, _hoveringIncoming ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0, 0f))
        {
            CkGui.InlineSpacingInner();
            CkGui.FramedIconText(expanded ? FAI.CaretDown : FAI.CaretRight);

            ImGui.SameLine();
            CkGui.FramedIconText(FAI.Inbox);
            using (ImRaii.PushFont(UiBuilder.MonoFont))
                CkGui.TextFrameAlignedInline($"{INCOMING_ID} ({_requests.TotalIncoming})");
        }
        _hoveringIncoming = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked())
            _config.ToggleDefaultFolder(INCOMING_ID);

        ImGui.Separator();
        if (!expanded || _incoming.Count is 0)
            return;

        using var indent = ImRaii.PushIndent(CkGui.IconSize(FAI.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, false);
        foreach (var entry in _incoming)
            entry.DrawRequestEntry(false);

        ImGui.Separator();
    }

    private void DrawOutgoing()
    {
        var expanded = _config.IsDefaultExpanded(OUTGOING_ID);

        using var id = ImRaii.PushId("folder_" + OUTGOING_ID);
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());

        using (CkRaii.Child($"folder__{OUTGOING_ID}", childSize, _hoveringOutgoing ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0, 0f))
        {
            CkGui.InlineSpacingInner();
            CkGui.FramedIconText(expanded ? FAI.CaretDown : FAI.CaretRight);

            ImGui.SameLine();
            CkGui.FramedIconText(FAI.Inbox);
            using (ImRaii.PushFont(UiBuilder.MonoFont))
                CkGui.TextFrameAlignedInline($"{OUTGOING_ID} ({_requests.TotalOutgoing})");
        }
        _hoveringOutgoing = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked())
            _config.ToggleDefaultFolder(OUTGOING_ID);

        ImGui.Separator();
        if (!expanded || _outgoing.Count is 0)
            return;

        using var indent = ImRaii.PushIndent(CkGui.IconSize(FAI.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, false);
        foreach (var entry in _outgoing)
            entry.DrawRequestEntry(true);

        ImGui.Separator();
    }

    private void RecreateRequests()
    {
        _incoming = _requests.Incoming.Select(r => new DrawSundesmoRequest(INCOMING_ID + r.User.UID + r.Target.UID, r, _hub, _requests, _sundesmos)).ToImmutableList();
        _outgoing = _requests.Outgoing.Select(r => new DrawSundesmoRequest(OUTGOING_ID + r.User.UID + r.Target.UID, r, _hub, _requests, _sundesmos)).ToImmutableList();
    }
}