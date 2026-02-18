using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui.Text;
using Sundouleia.Interop;
using Sundouleia.ModularActor;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;

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
    private readonly GPoseHandler _handler;
    private readonly GPoseManager _manager;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;
    private readonly UiFileDialogService _dialog;

    // Used to validate an imported file for decryption.
    private unsafe GameObject* _selectedGPoseActor = null;
    private ModularActorData? _selectedSmad;
    private ModularActorBase? _selectedSmab;
    private ModularActorOutfit? _selectedSmao;
    private ModularActorItem? _selectedSmai;

    public SMAControllerUI(ILogger<SMAControllerUI> logger, SundouleiaMediator mediator,
        SMAFileHandler fileHandler, GPoseHandler handler, GPoseManager manager,
        IpcManager ipc, CharaObjectWatcher watcher, UiFileDialogService dialog)
        : base(logger, mediator, "GPOSE - Modular Actor Control")
    {
        _fileHandler = fileHandler;
        _handler = handler;
        _manager = manager;
        _ipc = ipc;
        _watcher = watcher;
        _dialog = dialog;

        this.SetBoundaries(new(200, 400), ImGui.GetIO().DisplaySize);

        // Do not expose the SMA Information to prevent theft.
        Mediator.Subscribe<GPoseStartMessage>(this, _ =>
        {
#if DEBUG
            IsOpen = true;
#endif
        });

        Mediator.Subscribe<GPoseObjectDestroyed>(this, _ =>
        {
            unsafe
            {
                if (_selectedGPoseActor == (GameObject*)_.Address)
                    _selectedGPoseActor = null;
            }
        });

        Mediator.Subscribe<GPoseStartMessage>(this, _ =>
        {
#if DEBUG
            IsOpen = true;
#endif
        });

# if DEBUG
        IsOpen = true;
# endif
    }

    /// <summary>
    ///     Performed computation regardless of open state for a registered window. <para />
    ///     Helps us know if we should display this UI or not.
    /// </summary>
    public override void PreOpenCheck()
    {
        // Can maybe change this later, keep for debugging.
        //IsOpen = true; // OnTickService.InGPose;
    }

    protected override void PreDrawInternal()
    { }

    protected override void DrawInternal()
    {
        CkGui.FontText($"GPose Manager:", Fonts.UidFont);
        ImGui.Separator();
        
        DrawListBoxes();

        ImGui.SameLine();
        using var _ = ImRaii.Group();

        unsafe
        {
            CkGui.ColorTextFrameAligned("Selected GPose Actor:", ImGuiColors.DalamudYellow);
            CkGui.TextFrameAlignedInline(_selectedGPoseActor != null ? _selectedGPoseActor->NameString : "<No GPose Actor Selected>");
            // Otherwise draw options for them.
            if (CkGui.IconTextButton(FAI.Crosshairs, "Target"))
                _handler.GPoseTarget = _selectedGPoseActor;

            var handledEntry = _handler.AttachedActors.TryGetValue((nint)_selectedGPoseActor, out var entry) ? entry : null;

            ImGui.SameLine();
            CkGui.IconText(FAI.InfoCircle);
            var ttText = handledEntry is null
                ? "No SMA Data applied to this Actor."
                : $"SMA Data applied:" +
                $"\nLabel: {handledEntry.Data.Name}" +
                $"\nDescription: {handledEntry.Data.Description}" +
                $"\nCollectionID: {handledEntry.CollectionId}" +
                $"\nCPlusID: {handledEntry.CplusProfile}" +
                $"\nActorBase ID: {handledEntry.Data.Base.Id}";
            CkGui.AttachToolTip(ttText);

            ImGui.SameLine();
            if (CkGui.IconTextButton(FAI.Undo, "Remove SMA Data", disabled: UiService.DisableUI || handledEntry is null))
                _handler.DetachActor((nint)_selectedGPoseActor).ConfigureAwait(false);
            CkGui.AttachToolTip("Revert the applied SMA Data, removing them from the handled list.");
        }

        ImGui.Separator();
        CkGui.ColorTextFrameAligned("Selected Actor:", ImGuiColors.DalamudYellow);
        CkGui.TextFrameAlignedInline(_selectedSmad?.Description ?? "<No Actor Selected>");
        if (_selectedSmad is not { } selectedActor)
            return;

        if (CkGui.IconTextButton(FAI.ArrowsSpin, "Get Latest Allowances", disabled: UiService.DisableUI))
        {
            _logger.LogInformation("Future addition!");
        }
        CkGui.AttachToolTip("Retrieves the latest list of allowed data hashes for the selected SMA Base.");

        if (CkGui.IconTextButton(FAI.ArrowRight, "Apply to Target", disabled: UiService.DisableUI || _selectedSmad is null || !_handler.HasGPoseTarget))
            UiService.SetUITask(async () => await _handler.ApplySMAToGPoseTarget(selectedActor));
        CkGui.AttachToolTip("Applies the selected ActorBase to GPose Target.");

        ImGui.SameLine();
        if (CkGui.IconTextButton(FAI.Plus, "Spawn & Apply Data", disabled: UiService.DisableUI || _selectedSmad is null))
            UiService.SetUITask(async () => await _handler.SpawnAndApplySMAData(selectedActor));
        CkGui.AttachToolTip("Applies the selected ActorBase to a spawned BrioActor.");

        ImGui.Separator();
        foreach (var (path, replacement) in selectedActor.FileReplacements)
        {
            ImGui.Text($"Path: {path} -> Replacement: {replacement}");
        }
    }

    private void DrawListBoxes()
    {
        using var table = ImRaii.Table("sma_controller_table", 6, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit, new(800f, ImGui.GetContentRegionAvail().Y));
        if (!table)
            return;
        // Create a child for the first area.
        ImGui.TableSetupColumn("GPose Actors");
        ImGui.TableSetupColumn("Handled Actors");
        ImGui.TableSetupColumn("Loaded SMA Data");
        ImGui.TableSetupColumn("Loaded SMA Base");
        ImGui.TableSetupColumn("Loaded SMA Outfit");
        ImGui.TableSetupColumn("Loaded SMA Item");
        ImGui.TableHeadersRow();

        ImGui.TableNextColumn();
        DrawGPoseActors();
        ImGui.TableNextColumn();
        DrawHandledActors();
        ImGui.TableNextColumn();
        DrawLoadedSmadFiles();
        ImGui.TableNextColumn();
        DrawLoadedSmabFiles();
        ImGui.TableNextColumn();
        DrawLoadedSmaoFiles();
        ImGui.TableNextColumn();
        DrawLoadedSmaiFiles();
    }

    private unsafe void DrawGPoseActors()
    {
        // wa
        using var _ = ImRaii.ListBox("##gposers", new Vector2(125, ImGui.GetContentRegionAvail().Y));
        if (!_) return;

        foreach (var address in CharaObjectWatcher.GPoseActors)
        {
            var gposeActor = (GameObject*)address;
            if (ImGui.Selectable($"{gposeActor->NameString}##{gposeActor->NameString}{address}", gposeActor == _selectedGPoseActor))
                _selectedGPoseActor = gposeActor;
        }
    }

    private unsafe void DrawHandledActors()
    {
        using var _ = ImRaii.ListBox("##handledActors", new Vector2(125, ImGui.GetContentRegionAvail().Y));
        if (!_) return;

        foreach (var (address, entry) in _handler.AttachedActors)
            ImGui.Selectable($"{entry.Data.Name}({address:X})", false);
    }

    private void DrawLoadedSmadFiles()
    {
        using (ImRaii.ListBox("##smad_files", new Vector2(125, ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeightSpacing)))
        {
            foreach (var actor in _manager.SMAD)
                if (ImGui.Selectable($"{actor.Description}##smad_{actor.Description}", actor == _selectedSmad))
                    _selectedSmad = actor;
        }
        if (CkGui.IconTextButtonCentered(FontAwesomeIcon.FileImport, "Add Data File", 125))
        {
            _dialog.OpenSingleFilePicker("Import Actor Data File", "SMA Data{.smad}", (success, path) =>
            {
                if (!success) return;
                _manager.LoadSMADFile(path);
            });
        }
    }

    private void DrawLoadedSmabFiles()
    {
        using (ImRaii.ListBox("##smab_files", new Vector2(125, ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeightSpacing)))
        {
            foreach (var actorBase in _manager.Bases)
                if (ImGui.Selectable($"{actorBase.Description}##smab_{actorBase.Description}", actorBase == _selectedSmab))
                    _selectedSmab = actorBase;
        }

        if (CkGui.IconTextButtonCentered(FontAwesomeIcon.FileImport, "Add Base File", 125))
        {
            _dialog.OpenSingleFilePicker("Import Actor Base File", "SMA Base{.smab}", (success, path) =>
            {
                if (!success) return;
                _manager.LoadSMABFile(path);
            });
        }
    }

    private void DrawLoadedSmaoFiles()
    {
        using (ImRaii.ListBox("##smao_files", new Vector2(125, ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeightSpacing)))
        {
            foreach (var outfit in _manager.Outfits)
                if (ImGui.Selectable($"{outfit.Description}##smao_{outfit.Description}", outfit == _selectedSmao))
                    _selectedSmao = outfit;
        }
        if (CkGui.IconTextButtonCentered(FontAwesomeIcon.FileImport, "Add Outfit File", 125))
        {
            _dialog.OpenSingleFilePicker("Import Actor Outfit File", "SMA Outfit{.smao}", (success, path) =>
            {
                if (!success) return;
                _manager.LoadSMAOFile(path);
            });
        }
    }

    private void DrawLoadedSmaiFiles()
    {
        using (ImRaii.ListBox("##smai_files", new Vector2(125, ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeightSpacing)))
        {
            foreach (var item in _manager.Items)
                if (ImGui.Selectable($"{item.Description}##smai_{item.Description}", item == _selectedSmai))
                    _selectedSmai = item;
        }
        if (CkGui.IconTextButtonCentered(FontAwesomeIcon.FileImport, "Add Item File", 125))
        {
            _dialog.OpenSingleFilePicker("Import Actor Item File(s)", "SMA Item{.smai},SMA ItemPack{.smaip}", (success, path) =>
            {
                if (!success) return;
                // Load the item or item pack, based on the file type (add item packs later)
                _manager.LoadSMAIFile(path);
            });
        }
    }

    protected override void PostDrawInternal()
    { }
}