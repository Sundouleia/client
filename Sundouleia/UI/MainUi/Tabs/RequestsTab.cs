using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.MainWindow;

// this can easily become the "contact list" tab of the "main UI" window.
public class RequestsTab : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly SundesmoManager _sundesmoManager;
    private readonly DrawEntityFactory _factory;

    // Dont think we will need to worry about using folders since we are only drawing 2 request areas,
    // incoming and outgoing. Can modify this later, for right now we dont need to worry about
    // it too much since we have the code for request entries already.
    public RequestsTab(ILogger<RequestsTab> logger, SundouleiaMediator mediator,
        MainConfig config, SundesmoManager sundesmos, DrawEntityFactory factory)
        : base(logger, mediator)
    {
        _config = config;
        _sundesmoManager = sundesmos;
        _factory = factory;

        //Mediator.Subscribe<RefreshUiMessage>(this, _ => _drawFolders = GetDrawFolders());
        //_drawFolders = GetDrawFolders();
    }

    public void DrawRequestsSection()
    {
        // Will probably look very amateur compared to GSpeak's UI.
        DrawTypeSelector();
        ImGui.Separator();

        using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        CkGui.ColorTextCentered("TODO: Add Requests Here!", ImGuiColors.DalamudYellow);
    }

    // Picks between incoming and outgoing requests, so we know what to draw and such.
    private void DrawTypeSelector()
    {

    }

    // Could make a very easy generator if we just kept a static hash set of
    // pending requests in the MainHub with the connection response and then synced the list here.
}