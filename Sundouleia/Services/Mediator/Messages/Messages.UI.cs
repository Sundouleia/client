using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.State.Models;
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
///     Fires once we wish to open the popout permissions menu for a User pair.
/// </summary>
public record UserInteractionUiChangeMessage(Sundesmo User, InteractionsTab Type) : MessageBase;

/// <summary> 
///     To refresh the immutable list of draw pairs in the whitelist. <para />
///     If possible to avoid this, find a way!
/// </summary>
public record RefreshUiUsersMessage : MessageBase;

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
public record ProfileOpenStandaloneMessage(Sundesmo Pair) : MessageBase;

/// <summary> When the whitelist has a User hovered long enough and displays a profile, this is fired. </summary>
public record ProfilePopoutToggle(UserData? PairUserData) : MessageBase;

/// <summary> Removes the profile from the profile server for the defined user. </summary>
public record ClearProfileDataMessage(UserData? UserData = null) : MessageBase;

/// <summary> This is fired whenever the discord bot wishes to send out an account verification to our client. </summary>
public record VerificationPopupMessage(VerificationCode VerificationCode) : MessageBase;

// Could maybe use these down the line if we want to make sure icons for sundesmo group folders but otherwise remove.
public record ReScanThumbnailFolder : MessageBase;
public record ThumbnailImageSelected(Guid SourceId, Vector2 ImgSize, string FileName) : MessageBase;
