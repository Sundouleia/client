using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia.ModFiles;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class DebugActiveStateUI : WindowMediatorSubscriberBase
{
    private readonly TransientResourceManager _transients;

    public DebugActiveStateUI(ILogger<DebugActiveStateUI> logger, SundouleiaMediator mediator,
        TransientResourceManager transients) : base(logger, mediator, "Active State Debug")
    {
        _transients = transients;

        IsOpen = true;
        this.SetBoundaries(new Vector2(625, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal() { }

    protected override void PostDrawInternal() { }

    protected override void DrawInternal()
    {
        CkGui.CenterColorTextAligned("W.I.P. - MAKE DEBUGGER FOR ACTIVE DATA.", ImGuiColors.DalamudRed);

        // Transient Resolurce Monitoring
        _transients.DrawTransientResources();

        // Semi-Transient Resource Monitoring
        _transients.DrawPersistantTransients();

    }
}
