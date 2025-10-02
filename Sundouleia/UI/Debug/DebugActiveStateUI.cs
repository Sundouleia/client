using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class DebugActiveStateUI : WindowMediatorSubscriberBase
{

    public DebugActiveStateUI(ILogger<DebugActiveStateUI> logger, SundouleiaMediator mediator)
        : base(logger, mediator, "Active State Debug")
    {

        // IsOpen = true;
        this.SetBoundaries(new Vector2(625, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal() { }

    protected override void PostDrawInternal() { }

    protected override void DrawInternal()
    {
        CkGui.CenterColorTextAligned("W.I.P. - MAKE DEBUGGER FOR ACTIVE DATA.", ImGuiColors.DalamudRed);

    }
}
