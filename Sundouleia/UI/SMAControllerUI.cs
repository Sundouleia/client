using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.ModularActorData;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

// Doing the hecking brainstorm voodoo

namespace Sundouleia.Gui;


/// <summary>
///     Shows only in GPose. Allows you to import Sundouleia Modular Actor files and
///     apply them to others, customizing their appearnace, if allowed.
/// </summary>
/// <remarks>
///     Could show the UI Outside GPose but only in an overview mode, maybe. <para />
///     The intent is for load-only processes, so they should not be always hardbound to the GPose state.
/// </remarks>
public class SMAControllerUI : WindowMediatorSubscriberBase
{
    private readonly ModularActorFileHandler _fileHandler;
    private readonly ModularActorHandler _smaHandler;
    private readonly ModularActorManager _manager;
    private readonly IpcManager _ipc;
    private readonly UiFileDialogService _dialogService;

    // Used to validate an imported file for decryption.
    private string _passwordForLoadedFile = string.Empty;

    public SMAControllerUI(ILogger<SMAControllerUI> logger, SundouleiaMediator mediator,
        ModularActorFileHandler fileHandler, ModularActorHandler handler,
        ModularActorManager manager, IpcManager ipc, UiFileDialogService dialogService)
        : base(logger, mediator, "GPOSE - Modular Actor Control")
    {
        _fileHandler = fileHandler;
        _smaHandler = handler;
        _manager = manager;
        _ipc = ipc;
        _dialogService = dialogService;

        this.SetBoundaries(new(200, 400), ImGui.GetIO().DisplaySize);

        Mediator.Subscribe<GPoseStartMessage>(this, _ => IsOpen = true);
    }

    /// <summary>
    ///     Performed comutation regardless of open state for a registered window. <para />
    ///     Helps us know if we should display this UI or not.
    /// </summary>
    public override void PreOpenCheck()
    {
        // Can maybe change this later, idk.
        // IsOpen = OnTickService.InGPose;
    }

    protected override void PreDrawInternal()
    { }

    protected override void DrawInternal()
    {
        // Start small, expand as you go...
        ImGui.TextWrapped("Use this interface to import and apply Sundouleia Modular Actor files to characters in GPose.");

        ImGui.Separator();
        CkGui.FontText($"Imported SMA Files:", UiFontService.UidFont);

        // Password input.
        if (CkGui.IconTextButton(FontAwesomeIcon.FileImport, "Import SMAB"))
        {
            _dialogService.OpenSingleFilePicker("Import ActorBase", ".smab", (success, path) =>
            {
                if (!success)
                    return;
                // Try and import it. This could fail for various reasons.
                _fileHandler.LoadActorBaseFile(path, _passwordForLoadedFile);
            });
        }
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * .65f);
        ImGui.InputTextWithHint("##smapassword", "Password for imported files", ref _passwordForLoadedFile, 128);

        foreach (var smad in _smaHandler.ProcessedData)
        {
            CkGui.IconText(FAI.User);
            CkGui.TextFrameAlignedInline(smad.Description);
        }
        ImGui.Separator();
        CkGui.FontText($"GPoseActors:", UiFontService.UidFont);

    }

    protected override void PostDrawInternal()
    { }
}