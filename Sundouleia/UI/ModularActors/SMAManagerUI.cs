using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.DrawSystem;
using Sundouleia.ModFiles;
using Sundouleia.ModularActor;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class SMAManagerUI : WindowMediatorSubscriberBase
{
    // Some config, probably.
    private readonly SMAFileHandler _smaHandler;
    private readonly FileCacheManager _fileCache;
    private readonly SMAManager _smaManager;
    private readonly TutorialService _guides;

    public SMAManagerUI(ILogger<SMACreatorUI> logger, SundouleiaMediator mediator,
        SMAFileHandler smaHandler, FileCacheManager fileCache,
        SMAManager smaManager, TutorialService guides) 
        : base(logger, mediator, "Modular Actor Manager###SundouleiaSMAManager")
    {
        _smaHandler = smaHandler;
        _fileCache = fileCache;
        _smaManager = smaManager;
        _guides = guides;

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(500, 300), ImGui.GetIO().DisplaySize);
        // Add tutorial later.
    }
    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        ImGui.Text("BAGAGWA TWO POINT ZERO");
    }
}