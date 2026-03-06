using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Party;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.DrawSystem;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.Loci;

public class LociSettings
{
    private readonly ILogger<LociSettings> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly LociManager _manager;
    private readonly StatusesFS _statusFileSystem;
    private readonly PresetsFS _presetFileSystem;

    public LociSettings(ILogger<LociSettings> logger, SundouleiaMediator mediator,
        MainConfig config, LociManager manager, StatusesFS statusFS, PresetsFS presetFS)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
        _manager = manager;
        _statusFileSystem = statusFS;
        _presetFileSystem = presetFS;
    }

    public void DrawSettings()
    {
        CkGui.FontText("Functionality", Fonts.Default150Percent);
        var enabled = _config.Current.LociEnabled;
        if (ImGui.Checkbox($"Enable Module", ref enabled))
        {
            _config.Current.LociEnabled = enabled;
            _config.Save();
            _mediator.Publish(new LociEnabledStateChanged(enabled));
        }
        DrawIndentedEnables();

        CkGui.FontText("Limiters", Fonts.Default150Percent);
        var offInDuty = _config.Current.LociOffInDuty;
        if (ImGui.Checkbox("Disable in Duties/Instances", ref offInDuty))
        {
            _config.Current.LociOffInDuty = offInDuty;
            _config.Save();
        }

        var offInCombat = _config.Current.LociOffInCombat;
        if (ImGui.Checkbox("Disable in Combat", ref offInCombat))
        {
            _config.Current.LociOffInCombat = offInCombat;
            _config.Save();
        }

        var canEsuna = _config.Current.LociAllowEsuna;
        if (ImGui.Checkbox("Allow esunable statuses", ref canEsuna))
        {
            _config.Current.LociAllowEsuna = canEsuna;
            _config.Save();
        }

        var othersCanEsuna = _config.Current.LociOthersCanEsuna;
        if (ImGui.Checkbox("Others can Esuna your statuses", ref othersCanEsuna))
        {
            _config.Current.LociOthersCanEsuna = othersCanEsuna;
            _config.Save();
        }

        DrawMigrate();
    }

    private void DrawIndentedEnables()
    {
        using var dis = ImRaii.Disabled(!_config.Current.LociEnabled);
        using var indent = ImRaii.PushIndent();

        var vfxOn = _config.Current.LociSheVfxEnabled;
        var vfxLimited = _config.Current.LociSheVfxRestricted;
        var flyTextOn = _config.Current.LociFlyText;
        var flyTextLimit = _config.Current.LociFlyTextLimit;

        if (ImGui.Checkbox($"Loci VFX", ref vfxOn))
        {
            _config.Current.LociSheVfxEnabled = vfxOn;
            _config.Save();
        }
        CkGui.AttachToolTip("If VFX are applied on Loci Status application");

        if (ImGui.Checkbox($"Restrict VFX", ref vfxLimited))
        {
            _config.Current.LociSheVfxRestricted = vfxLimited;
            _config.Save();
        }
        CkGui.AttachToolTip("Restricts Vfx to only friends, party and nearby actors");

        if (ImGui.Checkbox($"Fly/Popup Text", ref flyTextOn))
        {
            _config.Current.LociFlyText = flyTextOn;
            _config.Save();
        }

        ImGui.SetNextItemWidth(150f);
        if (ImGui.SliderInt("Fly/Popup Text Limit", ref flyTextLimit, 5, 20))
        {
            _config.Current.LociFlyTextLimit = flyTextLimit;
            _config.Save();
        }
        CkGui.AttachToolTip("How many Fly/Popup Texts can be active simultaneously.");
    }

    private void DrawMigrate()
    {
        if (!OtherDirectoryExists())
            return;

        ImGui.Separator();
        var shiftAndCtrlPressed = ImGui.GetIO().KeyShift && ImGui.GetIO().KeyCtrl;

        if (CkGui.IconTextButton(FAI.FileImport, "Import Statuses", disabled: !shiftAndCtrlPressed))
        {
            var statusFS = GetMigratableFile("MoodleFileSystem.json");
            var statuses = GetMigratableFile("DefaultConfig.json");
            if (File.Exists(statusFS) && File.Exists(statuses))
            {
                _logger.LogInformation($"Migrating from {statusFS}");
                try
                {
                    var defaultJson = JObject.Parse(File.ReadAllText(statuses));
                    _manager.MigrateStatusesFromConfig(defaultJson);
                    _statusFileSystem.MergeWithMigratableFile(statusFS);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to migrate statuses from {statuses}");
                }
            }
        }
        CkGui.AttachToolTip("Migrate all statuses to Loci." +
            "--SEP----COL--Must hold CTRL+SHIFT to execute.--COL--", ImGuiColors.DalamudOrange);

        if (CkGui.IconTextButton(FAI.FileImport, "Import Presets", disabled: !shiftAndCtrlPressed))
        {
            var presetFS = GetMigratableFile("PresetFileSystem.json");
            var presets = GetMigratableFile("DefaultConfig.json");
            if (File.Exists(presetFS) && File.Exists(presets))
            {
                _logger.LogInformation($"Migrating from {presetFS}");
                try
                {
                    var defaultJson = JObject.Parse(File.ReadAllText(presets));
                    _manager.MigratePresetsFromConfig(defaultJson);
                    // Then update the FS.
                    _presetFileSystem.MergeWithMigratableFile(presetFS);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to migrate presets from {presets}");
                }
            }
        }
        CkGui.AttachToolTip("Migrate all presets to Loci." +
            "--SEP----COL--Must hold CTRL+SHIFT to execute.--COL--", ImGuiColors.DalamudOrange);
    }

    #region Helpers
    // Locate if we are able to migrate
    private string GetMigratableDirectoryPath()
    {
        var parentDir = Path.GetDirectoryName(ConfigFileProvider.SundouleiaDirectory);
        if (parentDir is null)
            return string.Empty;
        return Path.Combine(parentDir, "Moodles");
    }

    private string GetMigratableFile(string fileName)
        => Path.Combine(GetMigratableDirectoryPath(), fileName);

    private bool OtherDirectoryExists()
        => Directory.Exists(GetMigratableDirectoryPath());
    #endregion Helpers
}
