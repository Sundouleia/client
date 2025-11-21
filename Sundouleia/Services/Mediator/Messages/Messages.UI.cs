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

/// <summary> Basic UI Toggle </summary>
public record UiToggleMessage(Type UiType, ToggleType ToggleType = ToggleType.Toggle) : MessageBase;

/// <summary> Close all windows and open the IntroUI </summary>
public record SwitchToIntroUiMessage : MessageBase;

/// <summary> Forcefully opens Main UI, and closes the Introduction UI if opened. </summary>
public record SwitchToMainUiMessage : MessageBase;

/// <summary> Swaps the displayed whitelist FolderSystem between the GroupFolderSystem and WhitelistFolderSystem. </summary>
public record SwapWhitelistDDS : MessageBase;

/// <summary> Requests to the popup handler to display a report profile prompt. </summary>
public record OpenReportUIMessage(UserData UserToReport, ReportKind Kind) : MessageBase;

/// <summary> Sets the tab of the MainUI. </summary>
public record MainWindowTabChangeMessage(MainMenuTabs.SelectedTab NewTab) : MessageBase;

/// <summary> Should fire whenever the Main UI closes. Useful for the interactions popout. </summary>
public record FolderDragDropComplete(IDynamicFolder Source, IDynamicFolder Dest, List<IDrawEntity> Transferred) : MessageBase;

/// <summary> When we want a specific window removed. Most beneficial for profiles. </summary>
public record RemoveWindowMessage(WindowMediatorSubscriberBase Window) : MessageBase;

/// <summary> When a standalone profile UI is created. </summary>
public record ProfileOpenMessage(UserData UserData) : MessageBase;

public record CloseInteractionUi : MessageBase;

/// <summary> When a standalone profile UI is created. </summary>
public record ToggleSundesmoInteractionUI(Sundesmo Sundesmo, ToggleType NewState) : MessageBase;

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