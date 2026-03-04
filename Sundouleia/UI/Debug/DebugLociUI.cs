using Dalamud.Bindings.ImGui;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;

namespace Sundouleia.Gui;

public class DebugLociUI : WindowMediatorSubscriberBase
{
    private readonly CharaWatcher _watcher;
    
    public DebugLociUI(ILogger<DebugLociUI> logger, SundouleiaMediator mediator, CharaWatcher watcher)
        : base(logger, mediator, "Loci Debugger")
    {
        _watcher = watcher;
        this.SetBoundaries(new Vector2(625, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {

    }
}
