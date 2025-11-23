using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Microsoft.VisualBasic;
using OtterGui;
using OtterGui.Text;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Util;
using System.Collections.Immutable;

namespace Sundouleia.Gui.MainWindow;

// The UI element that will be continuously drawn alongside the MainUI under certain conditions.
// This can house various displays based on the selected tab, under spesific conditions.
// Overtime the conditions that trigger displays will be expanded upon.
public class MainSideUI : WindowMediatorSubscriberBase
{
    private readonly MainMenuTabs _mainUiTabs;
    private readonly MainHub _hub;
    private readonly SundesmoManager _sundesmos;

    public MainSideUI(ILogger<MainSideUI> logger, SundouleiaMediator mediator,
        MainMenuTabs tabs, MainHub hub, SundesmoManager sundesmos)
        : base(logger, mediator, "##SundouleiaInteractionsUI")
    {
        _mainUiTabs = tabs;
        _hub = hub;
        _sundesmos = sundesmos;

        Flags = WFlags.NoCollapse | WFlags.NoTitleBar | WFlags.NoResize | WFlags.NoScrollbar;
        IsOpen = false;

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            // Set the sundesmo to null, clearing draw functionality.
            // Do not restore this between loads, as the window is not that critical to keep alive (yet)
            _sundesmo = null;
            // Force close the UI.
            IsOpen = false;
        });

        Mediator.Subscribe<CloseInteractionUi>(this, _ =>
        {
            _logger.LogInformation("Closing InteractionsUI via CloseInteractionUi Message.");
            _sundesmo = null;
            IsOpen = false;
        });

        Mediator.Subscribe<MainWindowTabChangeMessage>(this, _ =>
        {
            if (_.NewTab is not MainMenuTabs.SelectedTab.Whitelist && IsOpen)
            {
                _logger.LogTrace("Closing InteractionsUI via MainWindowTabChangeMessage.");
                _sundesmo = null;
                IsOpen = false;
            }
        });

        Mediator.Subscribe<ToggleSundesmoInteractionUI>(this, _ =>
        {
            if (_sundesmos.GetUserOrDefault(_.Sundesmo.UserData) is not { } match)
                return;
            // Get if this is different.
            var isDiffSundesmo = _sundesmo is null || _sundesmo.UserData.UID != match.UserData.UID;

            // Update if different.
            if (isDiffSundesmo)
                _sundesmo = match;

            // If we satisfy any condition to open the UI, we need to make sure all prerequisites are met.
            var shouldOpen = (_.NewState is ToggleType.Show) || (_.NewState is ToggleType.Toggle && !IsOpen) || isDiffSundesmo;

            // Do open logic if we should open
            if (shouldOpen)
            {
                _logger.LogTrace("Showing InteractionsUI via ToggleSundesmoInteractionUI Message.");
                // Ensure MainUI is open
                Mediator.Publish(new UiToggleMessage(typeof(MainUI), ToggleType.Show));
                // Set the tab selection
                if (_mainUiTabs.TabSelection != MainMenuTabs.SelectedTab.Whitelist)
                    _mainUiTabs.TabSelection = MainMenuTabs.SelectedTab.Whitelist;
                // Open the window.
                IsOpen = true;
            }
            else if (_.NewState is ToggleType.Toggle)
                IsOpen = !IsOpen;
            else
                IsOpen = false;
        });
    }

    // The current user. Will not draw the window if null.
    private Sundesmo? _sundesmo = null;
    private string _dispName = string.Empty;
    private float _windowWidth = 0f;
    protected override void PreDrawInternal()
    {
        // Magic that makes the sticky pair window move with the main UI.
        var position = MainUI.LastPos;
        position.X += MainUI.LastSize.X;
        position.Y += ImGui.GetFrameHeightWithSpacing();
        ImGui.SetNextWindowPos(position);

        Flags |= WFlags.NoMove;

        // Define the drawn parameters used so they are only calculated once per draw frame.
        _dispName = _sundesmo?.GetNickAliasOrUid() ?? "Anon. User";
        _windowWidth = (ImGui.CalcTextSize($"Preventing animations from {_dispName}").X + ImGui.GetFrameHeightWithSpacing()).AddWinPadX();
        // Set the size.
        ImGui.SetNextWindowSize(new Vector2(_windowWidth, MainUI.LastSize.Y - ImGui.GetFrameHeightWithSpacing() * 2));
    }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        // Shouldnt even be drawing at all if the sundesmo is null.
        if (_sundesmo is not { } s)
            return;

        using var _ = CkRaii.Child("InteractionsUI", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;
        DrawHeader();

        DrawCommon(width);
        ImGui.Separator();

        ImGui.Text("Permissions");
        DrawDistinctPermRow(s, _dispName, width, nameof(PairPerms.AllowAnimations), s.OwnPerms.AllowAnimations);
        DrawDistinctPermRow(s, _dispName, width, nameof(PairPerms.AllowSounds), s.OwnPerms.AllowSounds);
        DrawDistinctPermRow(s, _dispName, width, nameof(PairPerms.AllowVfx), s.OwnPerms.AllowVfx);

        ImGui.Separator();

        ImGui.Text("Pair Options");
        DrawPairOptions(_.InnerRegion.X);
    }

    private void DrawHeader()
    {
        CkGui.CenterText($"{_dispName}'s Interactions");
        var width = CkGui.IconSize(FAI.VolumeUp).X + CkGui.IconSize(FAI.Running).X + CkGui.IconSize(FAI.PersonBurst).X + ImUtf8.ItemInnerSpacing.X * 2;
        CkGui.SetCursorXtoCenter(width);

        var sounds = _sundesmo!.PairPerms.AllowSounds;
        var anims = _sundesmo.PairPerms.AllowAnimations;
        var vfx = _sundesmo.PairPerms.AllowVfx;

        CkGui.IconText(FAI.VolumeUp, sounds ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{_dispName} {(sounds ? "can hear your modded SFX/Music." : "disabled your modded SFX/Music.")}");
        ImUtf8.SameLineInner();
        CkGui.IconText(FAI.Running, anims ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{_dispName} {(anims ? "can see your modded animations." : "disabled your modded animations.")}");
        ImUtf8.SameLineInner();
        CkGui.IconText(FAI.PersonBurst, vfx ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        CkGui.AttachToolTip($"{_dispName} {(vfx ? "can see your modded VFX." : "disabled your modded VFX.")}");

        ImGui.Separator(); 
    }

    private void DrawCommon(float width)
    {
        var isPaused = _sundesmo!.IsPaused;
        if (!isPaused)
        {
            if (CkGui.IconTextButton(FAI.User, "Open Profile", width, true, UiService.DisableUI))
                Mediator.Publish(new ProfileOpenMessage(_sundesmo.UserData));
            CkGui.AttachToolTip($"Opens {_dispName}'s profile!");

            if (CkGui.IconTextButton(FAI.ExclamationTriangle, $"Report {_dispName}'s Profile", width, true, UiService.DisableUI))
                Mediator.Publish(new OpenReportUIMessage(_sundesmo.UserData, ReportKind.Profile));
            CkGui.AttachToolTip($"Snapshot {_dispName}'s Profile and make a report with its state.");
        }

        DrawDistinctPermRow(_sundesmo, _dispName, width, nameof(PairPerms.PauseVisuals), isPaused, false, true);
        CkGui.AttachToolTip($"{(!isPaused ? "Pause" : "Resume")} the rendering of {_dispName}'s modded appearance.");
    }

    private void DrawPairOptions(float width)
    {
        if (_sundesmo!.IsTemporary)
        {
            var blockButton = _sundesmo.UserPair.TempAccepterUID != MainHub.UID;
            if (CkGui.IconTextButton(FAI.Link, "Convert to Permanent Pair", width, true, blockButton))
                UiService.SetUITask(async () =>
                {
                    var res = await _hub.UserPersistPair(new(_sundesmo.UserData));
                    if (res.ErrorCode is not SundouleiaApiEc.Success)
                        _logger.LogWarning($"Failed to convert temporary pair for {_sundesmo.GetNickAliasOrUid()}. Reason: {res.ErrorCode}");
                    else
                    {
                        _logger.LogInformation($"Successfully converted temporary pair for {_sundesmo.GetNickAliasOrUid()}.");
                        _sundesmo.MarkAsPermanent();
                    }
                });

            var timeLeft = TimeSpan.FromDays(1) - (DateTime.UtcNow - _sundesmo.UserPair.CreatedAt);
            var autoDeleteText = $"Temp. Pairing Expires in --COL--{timeLeft.Days}d {timeLeft.Hours}h {timeLeft.Minutes}m--COL--";
            var ttStr = $"Makes a temporary pair permanent. --NL--{autoDeleteText}" +
                $"{(blockButton ? "--SEP----COL--Only the user who accepted the request can use this.--COL--" : string.Empty)}";
            CkGui.AttachToolTip(ttStr, color: ImGuiColors.DalamudYellow);
        }

        if (CkGui.IconTextButton(FAI.Trash, $"Remove {_dispName} from your Pairs", width, true, !KeyMonitor.CtrlPressed() || !KeyMonitor.ShiftPressed()))
            UiService.SetUITask(async () =>
            {
                var res = await _hub.UserRemovePair(new(_sundesmo.UserData));
                if (res.ErrorCode is not SundouleiaApiEc.Success)
                    _logger.LogWarning($"Failed to remove pair {_sundesmo.GetNickAliasOrUid()}. Reason: {res.ErrorCode}");
                else
                {
                    _logger.LogInformation($"Successfully removed pair {_sundesmo.GetNickAliasOrUid()}.");
                    ImGui.CloseCurrentPopup();
                }
            });
        CkGui.AttachToolTip($"Must hold --COL--CTRL & SHIFT to remove.", color: ImGuiColors.DalamudRed);
    }

    // Modular framework similar to GSpeak but vastly simplified.
    private void DrawDistinctPermRow(Sundesmo sundesmo, string dispName, float width, string permName, bool current, bool defaultTT = true, bool invertColors = false)
    {
        using var col = ImRaii.PushColor(ImGuiCol.Button, 0);
        var txtData = PermissionData[permName];
        var pos = ImGui.GetCursorScreenPos();
        var trueCol = invertColors ? CkColor.TriStateCross.Uint() : CkColor.TriStateCheck.Uint();
        var falseCol = invertColors ? CkColor.TriStateCheck.Uint() : CkColor.TriStateCross.Uint();

        if (ImGuiUtil.DrawDisabledButton("##pair" + permName, new Vector2(width, ImGui.GetFrameHeight()), string.Empty, UiService.DisableUI))
        {
            if (string.IsNullOrEmpty(permName))
                return;

            UiService.SetUITask(async () =>
            {
                if (await ChangeOwnUnique(permName, !current).ConfigureAwait(false))
                    _logger.LogInformation($"Successfully changed own permission {permName} to {!current} for {sundesmo.GetNickAliasOrUid()}.");
            });
        }

        ImGui.SetCursorScreenPos(pos);
        PrintButtonRichText(txtData, dispName, current, trueCol, falseCol);
        if (defaultTT)
            CkGui.AttachToolTip($"Toggle this preference for {_dispName}.");

    }

    private void PrintButtonRichText(PermInfo pdp, string dispName, bool current, uint trueCol, uint falseCol)
    {
        using var _ = ImRaii.Group();
        CkGui.FramedIconText(current ? pdp.TrueFAI : pdp.FalseFAI);
        ImGui.SameLine(0, 0);
        if (pdp.CondAfterLabel)
        {
            CkGui.TextFrameAligned($" {dispName}");
            ImGui.SameLine(0, 0);
            ImGui.Text($" {pdp.Suffix} ");
            ImGui.SameLine(0, 0);
            CkGui.ColorTextFrameAligned(current ? pdp.CondTrue : pdp.CondFalse, current ? trueCol : falseCol);
            ImGui.SameLine(0, 0);
            ImGui.Text(".");
        }
        else
        {
            CkGui.ColorTextFrameAligned($" {(current ? pdp.CondTrue : pdp.CondFalse)} ", current ? trueCol : falseCol);
            ImGui.SameLine(0, 0);
            ImGui.Text(pdp.Label);
            ImGui.SameLine(0, 0);
            ImGui.Text($" {dispName}.");
        }
    }

    private record PermInfo(FAI TrueFAI, FAI FalseFAI, string CondTrue, string CondFalse, string Label, bool CondAfterLabel, string Suffix = "");
    private readonly ImmutableDictionary<string, PermInfo> PermissionData = ImmutableDictionary<string, PermInfo>.Empty
        .Add(nameof(PairPerms.PauseVisuals), new PermInfo(FAI.Eye, FAI.EyeSlash, "Paused", "Unpaused", string.Empty, true, "is"))
        .Add(nameof(PairPerms.AllowAnimations), new PermInfo(FAI.Running, FAI.Ban, "Allowing", "Preventing", "animations from", false))
        .Add(nameof(PairPerms.AllowSounds), new PermInfo(FAI.VolumeUp, FAI.VolumeMute, "Allowing", "Preventing", "sounds from", false))
        .Add(nameof(PairPerms.AllowVfx), new PermInfo(FAI.PersonBurst, FAI.Ban, "Allowing", "Preventing", "VFX from", false));


    /// <summary>
    ///     Updates a client's own PairPermission for a defined Sundesmo client-side.
    ///     After the client-side change is made, it requests the change server side.
    ///     If any error occurs from the server-call, the value is reverted to its state before the change.
    /// </summary>
    public async Task<bool> ChangeOwnUnique(string propertyName, object newValue)
    {
        if (_sundesmo is null) return false;

        var type = _sundesmo.OwnPerms.GetType();
        var property = type.GetProperty(propertyName);
        if (property is null || !property.CanRead || !property.CanWrite)
            return false;

        // Initially, Before sending it off, store the current value.
        var currentValue = property.GetValue(_sundesmo.OwnPerms);

        try
        {
            // Update it before we send off for validation.
            if (!PropertyChanger.TrySetProperty(_sundesmo.OwnPerms, propertyName, newValue, out object? finalVal))
                throw new InvalidOperationException($"Failed to set property {propertyName} for self in PairPerms with value {newValue}.");

            if (finalVal is null)
                throw new InvalidOperationException($"Property {propertyName} in PairPerms, has the finalValue was null, which is not allowed.");

            // Now that it is updated client-side, attempt to make the change on the server, and get the hub response.
            HubResponse response = await _hub.ChangeUniquePerm(_sundesmo.UserData, propertyName, (bool)newValue);

            if (response.ErrorCode is not SundouleiaApiEc.Success)
                throw new InvalidOperationException($"Failed to change {propertyName} to {finalVal} for self. Reason: {response.ErrorCode}");
        }
        catch (InvalidOperationException ex)
        {
            Svc.Logger.Warning(ex.Message + "(Resetting to Previous Value)");
            property.SetValue(_sundesmo.OwnPerms, currentValue);
            return false;
        }

        return true;
    }
}
