using CkCommons;
using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.DrawSystem;
using Sundouleia.ModFiles;
using Sundouleia.ModularActor;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using System.Threading.Tasks;

namespace Sundouleia.Gui;

// Might phase out this window in favor of a our GroupsDrawer later.
public class SMACreatorUI : WindowMediatorSubscriberBase
{
    // Some config, probably.
    private readonly SMAFileHandler _smaHandler;
    private readonly FileCacheManager _fileCache;
    private readonly SMAManager _smaManager;
    private readonly ModdedStateManager _moddedState;
    private readonly CharaObjectWatcher _objWatcher;
    private readonly UiFileDialogService _dialogService;
    private readonly TutorialService _guides;

    private CancellationTokenSource _exportCTS = new();
    private Task? _exportTask = null;

    private string _filePassword = "password";
    private string _exportDescription = string.Empty;
    private OwnedObject _objToExport = OwnedObject.Player;
    // Should always be kept secret at all times!!!!!
    private string _lastSavedLocation = string.Empty;
    private string _lastExportedFilePassword = string.Empty;
    private byte[] _lastExportedKey = Array.Empty<byte>();

    public SMACreatorUI(ILogger<SMACreatorUI> logger, SundouleiaMediator mediator, ModdedStateManager moddedState,
        SMAFileHandler smaHandler, FileCacheManager fileCache,
        SMAManager smaManager, CharaObjectWatcher objWatcher,
        UiFileDialogService dialogService, TutorialService guides) 
        : base(logger, mediator, "Modular Actor Creator###SundouleiaSMACreator")
    {
        _moddedState = moddedState;
        _smaHandler = smaHandler;
        _fileCache = fileCache;
        _smaManager = smaManager;
        _objWatcher = objWatcher;
        _dialogService = dialogService;
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
        CkGui.FontText("Modular Actor Type", UiFontService.UidFont);

        if (CkGuiUtils.EnumCombo("Owned Object To Export", 200f, _objToExport, out var newObj, Enum.GetValues<OwnedObject>()))
            _objToExport = newObj;
        CkGui.AttachToolTip("Which of your owned objects will be exported.");

        ImGui.Spacing();
        ImGui.Separator();
        CkGui.FontText("Modular Actor Exporter", UiFontService.UidFont);
        CkGui.TextFrameAligned("Exporting Owned Actor:");
        CkGui.ColorTextFrameAlignedInline($"{_objToExport}", ImGuiColors.DalamudYellow);

        CkGui.TextFrameAligned("ActorBase file description:");
        ImGui.InputTextWithHint("##FileDesc", "(Optional) Provide Description...", ref _exportDescription, 100);

        ImGui.Spacing();
        if (CkGui.IconTextButton(FontAwesomeIcon.FileExport, "Export ActorBase as SMAB", disabled: _exportTask is not null && !_exportTask.IsCompleted))
        {
            var defaultName = "actorbase.smab";
            _dialogService.SaveFile("Export ActorBase to file", ".smab", defaultName, ".smab", OnFileExported, Directory.Exists(_lastSavedLocation) ? _lastSavedLocation : null, true);
        }
        if (_exportTask is not null && !_exportTask.IsCompleted)
        {
            CkGui.TextInline("Export Progress:");
            CkGui.ColorTextInline(_smaHandler.CurrentFile, ImGuiColors.DalamudViolet);
            CkGui.TextInline("(");
            CkGui.ColorTextInline(_smaHandler.CurrentFile, ImGuiColors.DalamudViolet);
            CkGui.TextInline($") ");
        }

        CkGui.ColorTextWrapped("Note: ActorBase's are for storing the face, makeup, nails, and skin." +
            "\nEnsure your actor is in smallclothes before exporting this file." +
            "\nYour chest and leg models will NOT be included for privacy reasons.", ImGuiColors.DalamudYellow);

        ImGui.Spacing();
        ImGui.Separator();
        CkGui.FontText("Last Export Data", UiFontService.UidFont);

        CkGui.TextFrameAligned("Last exported ActorBase FilePath: ");
        CkGui.FramedIconText(FAI.Folder);
        CkGui.TextFrameAlignedInline(_lastSavedLocation.IsNullOrEmpty() ? "<Nothing Exported>" : _lastSavedLocation);
        
        CkGui.TextFrameAligned("Password for last exported ActorBase: ");
        CkGui.FramedIconText(FAI.Lock);
        CkGui.TextFrameAlignedInline(_lastExportedFilePassword.IsNullOrEmpty() ? "<No Password Set>" : _lastExportedFilePassword);
        
        CkGui.TextFrameAligned("Last exported ActorBase Private Key: ");
        CkGui.FramedIconText(FAI.Key);
        CkGui.TextFrameAlignedInline(_lastExportedKey.Length is 0 ? "<No Key Generated>" : $"{_lastExportedKey}");
    }

    private void OnFileExported(bool success, string savedPath)
    {
        if (!success)
            return;

        // Update the last saved location.
        _lastSavedLocation = Path.GetDirectoryName(savedPath) ?? string.Empty;
        // Perform the export to this location.
        _exportCTS = _exportCTS.SafeCancelRecreate();
        _exportTask = Task.Run(async () =>
        {
            _logger.LogInformation($"Starting export of ActorBase to {savedPath}.");
            // var privateKey = await _smaHandler.SaveActorBaseFile(_objToExport, _exportDescription, savedPath, _filePassword);
            _lastExportedFilePassword = _filePassword;
            // _lastExportedKey = privateKey;
            _logger.LogInformation($"Completed export of ActorBase to {savedPath}.");
        }, _exportCTS.Token);
    }
}