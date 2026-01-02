using CkCommons;
using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using CkCommons.DrawSystem;
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
    private readonly MainConfig _config;
    private readonly SMAFileHandler _fileHandler;
    private readonly SMAFileManager _manager;
    private readonly UiFileDialogService _fileDialog;
    private readonly TutorialService _guides;

    private CancellationTokenSource _exportCTS = new();
    private Task? _exportTask = null;

    private string _fileName = "actorbase";
    private string _fileDesc = string.Empty;
    private OwnedObject _objToExport = OwnedObject.Player;

    public SMACreatorUI(ILogger<SMACreatorUI> logger, SundouleiaMediator mediator,
        MainConfig config, SMAFileHandler smaHandler, SMAFileManager smaManager,
        UiFileDialogService fileDialog, TutorialService guides) 
        : base(logger, mediator, "Modular Actor Creator###SundouleiaSMACreator")
    {
        _config = config;
        _fileHandler = smaHandler;
        _manager = smaManager;
        _fileDialog = fileDialog;
        _guides = guides;

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(500, 300), ImGui.GetIO().DisplaySize);
    }

    private bool IsExporting => _exportTask is not null && !_exportTask.IsCompleted;

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }
    
    protected override void DrawInternal()
    {
        CkGui.FontText("SMA Exporter", UiFontService.UidFont);
        ImGui.Separator();

        CkGui.FramedIconText(FAI.MapPin);
        CkGui.TextFrameAlignedInline("Export Location:");
        CkGui.ColorTextInline(_config.Current.SMAExportFolder, ImGuiColors.DalamudYellow);

        if (CkGuiUtils.EnumCombo("Owned Object To Export", 200f, _objToExport, out var newObj, Enum.GetValues<OwnedObject>()))
            _objToExport = newObj;
        CkGui.AttachToolTip("Which of your owned objects will be exported.");

        CkGui.FramedIconText(FAI.Heading);
        ImUtf8.SameLineInner();
        ImGui.InputTextWithHint("Name##FileName", "Provide file name.", ref _fileName, 50);

        CkGui.FramedIconText(FAI.AlignLeft);
        ImUtf8.SameLineInner();
        ImGui.InputTextWithHint("Description##FileDesc", "(Optional) Provide Description...", ref _fileDesc, 100);

        // Maybe update with savefile later when things are not centralized for testing but idk.
        if (CkGui.IconTextButton(FontAwesomeIcon.FileExport, "Export ActorBase (SMAB)", disabled: IsExporting))
        {
            _fileDialog.SaveFile("Export Sundouleia Modular Actor Base (SMAB)", "Actor Base{.smab}", _fileName, ".smab", (success, path) =>
            {
                if (!success)
                    return;
                ExportSMAB(path);
            }, Directory.Exists(_config.Current.SMAExportFolder) ? _config.Current.SMAExportFolder : null, true);
        }

        if (IsExporting)
        {
            CkGui.TextFrameAligned("Exporting File:");
            CkGui.ColorTextFrameAlignedInline(_fileHandler.CurrentFile, ImGuiColors.DalamudViolet);
            CkGui.TextFrameAligned($"Export Progress: ({_fileHandler.ScannedFiles} / {_fileHandler.TotalFiles})");
        }


        CkGui.ColorTextWrapped("Note: ActorBase's are for storing the face, makeup, nails, and skin." +
            "\nEnsure your actor is in smallclothes before exporting this file." +
            "\nYour chest and leg models will NOT be included for privacy reasons.", ImGuiColors.DalamudYellow);
    }

    private void ExportSMAB(string filePath)
    {
        if (IsExporting)
            return;

        // Extract the final, actual given name from the file.
        _fileName = Path.GetFileNameWithoutExtension(filePath);

        // Perform the export to this location.
        _exportCTS = _exportCTS.SafeCancelRecreate();
        _exportTask = Task.Run(async () =>
        {
            _logger.LogInformation($"Starting export of ActorBase to {filePath}.");
            // Process the file save.
            if (await _fileHandler.SaveSMABFile(_objToExport, filePath, _fileName, _fileDesc, _exportCTS.Token) is not { } summary)
            {
                _logger.LogWarning($"Failed to export ActorBase to {filePath}.");
                return;
            }

            _logger.LogInformation($"Completed export of ActorBase to {filePath}.");
            // Construct for saving, the modular actor owned metadata for out file.
            // This includes things like if it is in protected mode, the file key, or password.
            _manager.AddSavedBase(summary, filePath, string.Empty);
            _logger.LogInformation($"Registered exported ActorBase to owned files: {filePath}.");
        }, _exportCTS.Token);
    }
}