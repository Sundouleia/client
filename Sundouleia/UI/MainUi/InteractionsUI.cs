using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Util;

namespace Sundouleia.Gui.MainWindow;
public class InteractionsUI : WindowMediatorSubscriberBase
{
    private readonly MainHub _hub;
    public InteractionsUI(ILogger<InteractionsUI> logger, SundouleiaMediator mediator, MainHub hub)
        : base(logger, mediator, $"InteractionsUI")
    {
        _hub = hub;

        Flags = WFlags.NoCollapse | WFlags.NoTitleBar | WFlags.NoResize | WFlags.NoScrollbar;
        IsOpen = false;

        Mediator.Subscribe<TogglePermissionWindow>(this, msg =>
        {
            _sundesmo = msg.Sundesmo;
            IsOpen = _sundesmo is null;
        });
    }

    // The current user. Will not draw the window if null.
    private Sundesmo? _sundesmo = null;
    private string _dispName = string.Empty;
    private float _windowWidth => ImGuiHelpers.GlobalScale * 280f;


    protected override void PreDrawInternal()
    {
        // Magic that makes the sticky pair window move with the main UI.
        var position = MainUI.LastPos;
        position.X += MainUI.LastSize.X;
        position.Y += ImGui.GetFrameHeightWithSpacing();
        ImGui.SetNextWindowPos(position);

        Flags |= WFlags.NoMove;

        _dispName = _sundesmo?.GetNickAliasOrUid() ?? "Anon. User";
        ImGui.SetNextWindowSize(new Vector2(_windowWidth, MainUI.LastSize.Y - ImGui.GetFrameHeightWithSpacing() * 2));
    }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        // Shouldnt even be drawing at all if the sundesmo is null.
        if (_sundesmo is null)
            return;

        using var _ = CkRaii.Child("InteractionsUI", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = ImGui.GetContentRegionAvail().X;

        var isPaused = _sundesmo.IsPaused;
        if (!isPaused)
        {
            if (CkGui.IconTextButton(FAI.User, "Open Profile", width, true))
                Mediator.Publish(new ProfileOpenMessage(_sundesmo.UserData));
            CkGui.AttachToolTip($"Opens {_dispName}'s profile!");

            if (CkGui.IconTextButton(FAI.ExclamationTriangle, $"Report {_dispName}'s Profile", width, true))
                Mediator.Publish(new OpenReportUIMessage(_sundesmo.UserData, ReportKind.Profile));
            CkGui.AttachToolTip($"Snapshot {_dispName}'s Profile and make a report with its state.");
        }

        if (CkGui.IconTextButton(isPaused ? FAI.Play : FAI.Pause, $"{(isPaused ? "Unpause" : "Pause")} {_dispName}", width, true))
            UiService.SetUITask(async () => await ChangeOwnUnique(nameof(PairPerms.PauseVisuals), !isPaused));
        CkGui.AttachToolTip(!isPaused ? "Pause" : "Resume" + $"pairing with {_dispName}.");

        if (_sundesmo.PlayerRendered)
        {
            if (CkGui.IconTextButton(FAI.Sync, "Reload Appearance data", width, true))
                _sundesmo.ReapplyAlterations();
            CkGui.AttachToolTip("This reapplies the latest data from Customize+ and Moodles");
        }

        ImGui.Separator();
        if (CkGui.IconTextButton(FAI.Trash, "Unpair", width, true, !KeyMonitor.CtrlPressed() || !KeyMonitor.ShiftPressed()))
            UiService.SetUITask(async () => await _hub.UserRemovePair(new(_sundesmo.UserData)));
        CkGui.AttachToolTip($"Must hold --COL--CTRL & SHIFT to remove.", color: ImGuiColors.DalamudRed);
    }

    // TODO: Replace this placeholder fix.
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
