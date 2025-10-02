using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace Sundouleia.Services.Mediator;

/// <summary>
///     How we want to modify the defined UI window.
/// </summary>
public enum ToggleType
{
    Toggle,
    Show,
    Hide
}

/// <summary>
///     Toggle open / close / between sundesmos?
/// </summary>
public record TogglePermissionWindow(Sundesmo Sundesmo) : MessageBase;

/// <summary> 
///     To refresh the immutable list of draw pairs in the whitelist. <para />
///     If possible to avoid this, find a way!
/// </summary>
public record RefreshUiMessage : MessageBase;

/// <summary>
///     Fires whenever we need to refresh the created DrawRequests. <para />
///     This should not be needed anymore unless we are making another tab menu specifically for it.
/// </summary>
public record RefreshUiRequestsMessage : MessageBase;

/// <summary> Basic UI Toggle </summary>
public record UiToggleMessage(Type UiType, ToggleType ToggleType = ToggleType.Toggle) : MessageBase;

/// <summary> Close all windows and open the IntroUI </summary>
public record SwitchToIntroUiMessage : MessageBase;

/// <summary> Forcefully opens Main UI, and closes the Introduction UI if opened. </summary>
public record SwitchToMainUiMessage : MessageBase;

/// <summary> Requests to the popup handler to display a report profile prompt. </summary>
public record OpenReportUIMessage(UserData UserToReport, ReportKind Kind) : MessageBase;

/// <summary> Sets the tab of the MainUI. </summary>
public record MainWindowTabChangeMessage(MainMenuTabs.SelectedTab NewTab) : MessageBase;

/// <summary> Should fire whenever the Main UI closes. Useful for the interactions popout. </summary>
public record ClosedMainUiMessage : MessageBase;

/// <summary> When we want a specific window removed. Most beneficial for profiles. </summary>
public record RemoveWindowMessage(WindowMediatorSubscriberBase Window) : MessageBase;

/// <summary> When a standalone profile UI is created. </summary>
public record ProfileOpenMessage(UserData UserData) : MessageBase;

/// <summary> When the whitelist has a User hovered long enough and displays a profile, this is fired. </summary>
public record OpenProfilePopout(UserData UserData) : MessageBase;

/// <summary> When the profile popout is closed or needs to be toggled. </summary>
public record CloseProfilePopout : MessageBase;

/// <summary> Removes the profile from the profile server for the defined user. </summary>
public record ClearProfileDataMessage(UserData UserData) : MessageBase;

/// <summary> Primarily intended to be used by the toggle of our UI's 'ShowProfile' option. </summary>
public record ClearProfileCache : MessageBase;

/// <summary> This is fired whenever the discord bot wishes to send out an account verification to our client. </summary>
public record VerificationPopupMessage(VerificationCode VerificationCode) : MessageBase;