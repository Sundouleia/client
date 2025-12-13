using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui.Text;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.ModularActor;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;

// Doing the hecking brainstorm voodoo

namespace Sundouleia.Gui;


/// <summary>
///     Shows only in GPose. Allows you to import Sundouleia Modular Actor files and
///     apply them to others, customizing their appearance, if allowed.
/// </summary>
/// <remarks>
///     Could show the UI Outside GPose but only in an overview mode, maybe. <para />
///     The intent is for load-only processes, so they should not be always hardbound to the GPose state.
/// </remarks>
public class SMAControllerUI : WindowMediatorSubscriberBase
{
    private readonly SMAFileHandler _fileHandler;
    private readonly GPoseActorHandler _smaHandler;
    private readonly SMAManager _manager;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;
    private readonly UiFileDialogService _dialogService;

    // Used to validate an imported file for decryption.
    private ModularActorData? _selectedActor;
    private unsafe GameObject* _selectedGPoseActor = null;
    private string _passwordForLoadedFile = string.Empty;


    public SMAControllerUI(ILogger<SMAControllerUI> logger, SundouleiaMediator mediator,
        SMAFileHandler fileHandler, GPoseActorHandler handler,
        SMAManager manager, IpcManager ipc, CharaObjectWatcher watcher,
        UiFileDialogService dialogService)
        : base(logger, mediator, "GPOSE - Modular Actor Control")
    {
        _fileHandler = fileHandler;
        _smaHandler = handler;
        _manager = manager;
        _ipc = ipc;
        _dialogService = dialogService;

        this.SetBoundaries(new(200, 400), ImGui.GetIO().DisplaySize);

        Mediator.Subscribe<GPoseStartMessage>(this, _ => IsOpen = true);

        Mediator.Subscribe<GPoseObjectDestroyed>(this, _ =>
        {
            unsafe
            {
                if (_selectedGPoseActor == (GameObject*)_.Address)
                    _selectedGPoseActor = null;
            }
        });
    }

    /// <summary>
    ///     Performed computation regardless of open state for a registered window. <para />
    ///     Helps us know if we should display this UI or not.
    /// </summary>
    public override void PreOpenCheck()
    {
        // Can maybe change this later, keep for debugging.
        IsOpen = OnTickService.InGPose;
    }

    protected override void PreDrawInternal()
    { }

    protected override void DrawInternal()
    {
        CkGui.FontText($"Loaded SMA Files:", UiFontService.UidFont);
        ImGui.SameLine();
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

        ImGui.Separator();
        using var table = ImRaii.Table("sma_controller_table", 3, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit, ImGui.GetContentRegionAvail());
        if (!table)
            return;
        // Create a child for the first area.
        ImGui.TableSetupColumn("gpose_actors");
        ImGui.TableSetupColumn("loaded_sma");
        ImGui.TableSetupColumn("actor_options", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.Text("GPose Actors:");
        using (ImRaii.ListBox("##gposemodularActors", new Vector2(200, ImGui.GetContentRegionAvail().Y)))
        {
            unsafe
            {
                foreach (var address in CharaObjectWatcher.GPoseActors)
                {
                    var gposeActor = (GameObject*)address;
                    if (ImGui.Selectable(gposeActor->NameString, gposeActor == _selectedGPoseActor))
                        _selectedGPoseActor = gposeActor;
                }
            }
        }

        ImGui.TableNextColumn();
        var listboxHeight = (ImGui.GetContentRegionAvail().Y - ImUtf8.ItemSpacing.Y - ImUtf8.FrameHeightSpacing * 2 - ImUtf8.TextHeightSpacing * 2) / 2;
        ImGui.Text("Handled SMA Entries:");
        using (ImRaii.ListBox("##handledSMAEntries", new Vector2(150, listboxHeight)))
        {
            foreach (var (address, entry) in _smaHandler.HandledGPoseActors)
                ImGui.Selectable($"{entry.DisplayName}({address:X})", false);
        }

        ImGui.Text("Loaded SMA Data:");
        using (ImRaii.ListBox("##modularActors", new Vector2(150, listboxHeight)))
        {
            foreach (var actor in _manager.ProcessedActors)
            {
                if (ImGui.Selectable($"{actor.Description}##sma_{actor.Description}", actor == _selectedActor))
                    _selectedActor = actor;
            }
        }
        ImGui.SetNextItemWidth(150);
        ImGui.InputTextWithHint("##import-password", "File Password...", ref _passwordForLoadedFile, 100);
        // Draw out the buttons for loading here.
        if (CkGui.IconTextButtonCentered(FontAwesomeIcon.FileImport, "Import SMAB", 150))
        {
            _dialogService.OpenSingleFilePicker("Import ActorBase", ".smab", (success, path) =>
            {
                if (!success)
                    return;
                // Try and import it. This could fail for various reasons.
                _fileHandler.LoadActorBaseFile(path, _passwordForLoadedFile);
            });
        }

        ImGui.TableNextColumn();
        ImGui.Dummy(new(ImGui.GetContentRegionAvail().X, 0)); // Force alignment.
        unsafe
        {
            CkGui.ColorTextFrameAligned("Selected GPose Actor:", ImGuiColors.DalamudYellow);
            CkGui.TextFrameAlignedInline(_selectedGPoseActor != null ? _selectedGPoseActor->NameString : "<No GPose Actor Selected>");
            // Otherwise draw options for them.
            if (CkGui.IconTextButton(FAI.Crosshairs, "Target"))
                _smaHandler.GPoseTarget = _selectedGPoseActor;

            var handledEntry = _smaHandler.HandledGPoseActors.TryGetValue((nint)_selectedGPoseActor, out var entry) ? entry : null;

            ImGui.SameLine();
            CkGui.IconText(FAI.InfoCircle);
            var ttText = handledEntry is null
                ? "No SMA Data applied to this Actor."
                : $"SMA Data applied:" +
                $"\nLabel: {handledEntry.DisplayName}" +
                $"\nDescription: {handledEntry.Data.Description}" +
                $"\nCollectionID: {handledEntry.CollectionId}" +
                $"\nCPlusID: {handledEntry.CPlusId}" +
                $"\nActorBase ID: {handledEntry.Data.BaseId}";
            CkGui.AttachToolTip(ttText);

            ImGui.SameLine();
            if (CkGui.IconTextButton(FAI.Undo, "Remove SMA Data", disabled: UiService.DisableUI || handledEntry is null))
                _smaHandler.RemoveActor(handledEntry!).ConfigureAwait(false);
            CkGui.AttachToolTip("Revert the applied SMA Data, removing them from the handled list.");
        }

        ImGui.Separator();
        CkGui.ColorTextFrameAligned("Selected Actor:", ImGuiColors.DalamudYellow);
        CkGui.TextFrameAlignedInline(_selectedActor?.Description ?? "<No Actor Selected>");
        if (_selectedActor is not { } selectedActor)
            return;

        if (CkGui.IconTextButton(FAI.ArrowsSpin, "Get Latest Allowances", disabled: UiService.DisableUI))
        {
            _logger.LogInformation("Future addition!");
        }
        CkGui.AttachToolTip("Retrieves the latest list of allowed data hashes for the selected SMA Base.");

        if (CkGui.IconTextButton(FAI.ArrowRight, "Apply to Target", disabled: UiService.DisableUI || _selectedActor is null || !_smaHandler.HasGPoseTarget))
            UiService.SetUITask(async () => await _manager.ApplySMAToGPoseTarget(selectedActor));
        CkGui.AttachToolTip("Applies the selected ActorBase to GPose Target.");

        ImGui.SameLine();
        if (CkGui.IconTextButton(FAI.Plus, "Spawn & Apply Data", disabled: UiService.DisableUI || _selectedActor is null))
            UiService.SetUITask(async () => await _manager.SpawnAndApplySMAData(selectedActor));
        CkGui.AttachToolTip("Applies the selected ActorBase to a spawned BrioActor.");
    }

    protected override void PostDrawInternal()
    { }
}