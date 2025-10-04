using CheapLoc;

namespace Sundouleia.Localization
{
    internal static class CkLoc
    {
        public static Intro Intro { get; set; } = new();
        public static Tutorials Tutorials { get; set; } = new();
        public static CoreUi CoreUi { get; set; } = new();
        public static Settings Settings { get; set; } = new();

        public static void ReInitialize()
        {
            Intro = new Intro();
            Tutorials = new Tutorials();
            CoreUi = new CoreUi();
            Settings = new Settings();
        }
    }

    #region Intro
    public class Intro
    {
        public ToS ToS { get; set; } = new();
        public Register Register { get; set; } = new();
    }

    // Get to Last.
    public class ToS
    {
        public readonly string Title = Loc.Localize("ToS_Title", "ACTUAL LABEL HERE");
    }

    // Get to Last.
    public class Register
    {
        public readonly string Title = Loc.Localize("Register_Title", "ACTUAL LABEL HERE");
    }
    #endregion Intro

    #region Tutorials
    // Can add more as time goes on and stuff.
    public class Tutorials
    {
        public HelpMainUi MainUi { get; set; } = new();
        public HelpGroups Groups { get; set; } = new();
    }

    public class HelpMainUi
    {
        public readonly string Step1Title = Loc.Localize("HelpMainUi_Step1Title", "Startup Tutorial");
        public readonly string Step1Desc = Loc.Localize("HelpMainUi_Step1Desc", "Welcome to Sundouleia! This many Users are currently online!");
        public readonly string Step1DescExtended = Loc.Localize("HelpMainUi_Step1DescExtended", "To view other tutorials like this, click (?) icons on window headers!");

        public readonly string Step2Title = Loc.Localize("HelpMainUi_Step2Title", "Connection State");
        public readonly string Step2Desc = Loc.Localize("HelpMainUi_Step2Desc", "Your current connection status.");
        public readonly string Step2DescExtended = Loc.Localize("HelpMainUi_Step2DescExtended", "You can hover over this button for more details.");

        public readonly string Step3Title = Loc.Localize("HelpMainUi_Step3Title", "Homepage");
        public readonly string Step3Desc = Loc.Localize("HelpMainUi_Step3Desc", "Quick-Access to various modules of Sundouleia from here.");

        public readonly string Step4Title = Loc.Localize("HelpMainUi_Step4Title", "Whitelist");
        public readonly string Step4Desc = Loc.Localize("HelpMainUi_Step4Desc", "Where your added pairs appear.");
        public readonly string Step4DescExtended = Loc.Localize("HelpMainUi_Step4DescExtended",
            "MIDDLE-CLICK => Open this User's Profile.\n" +
            "RIGHT-CLICK => Set a nickname for this User.\n" +
            "Triple Dots => View Interactable actions for this User.");

        public readonly string Step5Title = Loc.Localize("HelpMainUi_Step5Title", "Adding Users");
        public readonly string Step5Desc = Loc.Localize("HelpMainUi_Step5Desc", "Send out requests here.");
        public readonly string Step5DescExtended = Loc.Localize("HelpMainUi_Step5DescExtended", "Sent requests expire after " +
            "3 days if not responded to, and can also be canceled at anytime.");

        public readonly string Step6Title = Loc.Localize("HelpMainUi_Step6Title", "Attaching Messages");
        public readonly string Step6Desc = Loc.Localize("HelpMainUi_Step6Desc", "Messages can be attached to requests, if desired.");
        public readonly string Step6DescExtended = Loc.Localize("HelpMainUi_Step6DescExtended", "These can provide context for who's sending " +
            "the request, helping inform the recipient who you are!");

        public readonly string Step7Title = Loc.Localize("HelpMainUi_Step7Title", "Requests");
        public readonly string Step7Desc = Loc.Localize("HelpMainUi_Step7Desc", "View any incoming or outgoing requests here.");
        public readonly string Step7DescExtended = Loc.Localize("HelpMainUi_Step7DescExtended", "You can accept, decline, or cancel requests here.");

        public readonly string Step8Title = Loc.Localize("HelpMainUi_Step7Title", "The Radar");
        public readonly string Step8Desc = Loc.Localize("HelpMainUi_Step7Desc", "Radars allow for proximity-based easy pairing in gathered groups, and temporary pair requesting.\n" +
            "Radars are made by design to be tedious as a groups size grows, which helps ease the concern of non-sundouleia users feeling left out in larger community attractions / venues.");

        public readonly string Step9Title = Loc.Localize("HelpMainUi_Step8Title", "Radar Users");
        public readonly string Step9Desc = Loc.Localize("HelpMainUi_Step8Desc", "Other Users with the Radar feature enabled will appear below when " +
            "in the same world/area as you.");
        public readonly string Step9DescExtended = Loc.Localize("HelpMainUi_Step8DescExtended", "You can send temp pairing request to them in this window.");

        public readonly string Step10Title = Loc.Localize("HelpMainUi_Step9Title", "Radar Chat");
        public readonly string Step10Desc = Loc.Localize("HelpMainUi_Step9Desc", "A Chat distinct to your World/Territory location. You can chat with " +
            "other users here.");

        public readonly string Step11Title = Loc.Localize("HelpMainUi_Step10Title", "Chat Rules");
        public readonly string Step11Desc = Loc.Localize("HelpMainUi_Step10Desc", "As with any public online chat, there are rules. You can review them here.");
        public readonly string Step11DescExtended = Loc.Localize("HelpMainUi_Step10DescExtended", "Misconduct in chat can be reported by any other user that sees it. " +
            "Reports are reviewed by the Sundouleia team fairly based on context and account strike history.");

        public readonly string Step12Title = Loc.Localize("HelpMainUi_Step11Title", "Chat Privacy");
        public readonly string Step12Desc = Loc.Localize("HelpMainUi_Step11Desc", "Anything you send here displays your name anonymously. Only the last 3 characters of your UID are displayed.");

        public readonly string Step13Title = Loc.Localize("HelpMainUi_Step12Title", "Chat Emotes");
        public readonly string Step13Desc = Loc.Localize("HelpMainUi_Step12Desc", "View the emote menu with this button! You can click any inside it to append it to your message");
        public readonly string Step13DescExtended = Loc.Localize("HelpMainUi_Step12DescExtended", "Emotes can also be manually added to chat messages by typing out emotes like discord emotes. :cheer:");

        public readonly string Step14Title = Loc.Localize("HelpMainUi_Step13Title", "Chat Scroll");
        public readonly string Step14Desc = Loc.Localize("HelpMainUi_Step13Desc", "Sets if the window will always auto-scroll to the last sent message.");
        public readonly string Step14DescExtended = Loc.Localize("HelpMainUi_Step13DescExtended", "Turning Auto-Scroll off lets you scroll up freely.");

        public readonly string Step15Title = Loc.Localize("HelpMainUi_Step14Title", "Examining Users");
        public readonly string Step15Desc = Loc.Localize("HelpMainUi_Step14Desc", "Hovering the Anon.User-XXX of a message lets you see when it was sent. Middle-Click to view their profile!");
        public readonly string Step15DescExtended = Loc.Localize("HelpMainUi_Step14DescExtended", "You can CTRL+SHIFT+RCLICK to block the user as well, hiding all chat and preventing requests.");

        public readonly string Step16Title = Loc.Localize("HelpMainUi_Step15Title", "Account Page");
        public readonly string Step16Desc = Loc.Localize("HelpMainUi_Step15Desc", "Manage account settings here.");
        public readonly string Step16DescExtended = Loc.Localize("HelpMainUi_Step15DescExtended", "This page contains important information " +
            "about you, and access to profile setup, configs, and support links!");

        public readonly string Step17Title = Loc.Localize("HelpMainUi_Step16Title", "Client UID");
        public readonly string Step17Desc = Loc.Localize("HelpMainUi_Step16Desc", "Your UID for pairing.");
        public readonly string Step17DescExtended = Loc.Localize("HelpMainUi_Step16DescExtended", "This defines your account, " +
            "you shouldn't display this in global chats or profiles.");

        public readonly string Step18Title = Loc.Localize("HelpMainUi_Step17Title", "Profile Editing");
        public readonly string Step18Desc = Loc.Localize("HelpMainUi_Step17Desc", "Make Customizations to your Profile here.");
        public readonly string Step18DescExtended = Loc.Localize("HelpMainUi_Step17DescExtended", "You can customize the display of your profile, description, and Avatar here.");

        public readonly string Step19Title = Loc.Localize("HelpMainUi_Step18Title", "Profile Publicity");
        public readonly string Step19Desc = Loc.Localize("HelpMainUi_Step18Desc", "If a profile is public, it can be viewed in Radar Chat.");
        public readonly string Step19DescExtended = Loc.Localize("HelpMainUi_Step18DescExtended", "Private profile can only be viewed by yourself and your User pairs.");

        public readonly string Step20Title = Loc.Localize("HelpMainUi_Step19Title", "Profile Titles");
        public readonly string Step20Desc = Loc.Localize("HelpMainUi_Step19Desc", "Earned through Achievements, which are still a WIP (or may make it obtained via other means)");

        public readonly string Step21Title = Loc.Localize("HelpMainUi_Step20Title", "Profile Customization");
        public readonly string Step21Desc = Loc.Localize("HelpMainUi_Step20Desc", "Unlocked from Achievements! (WIP)");
        public readonly string Step21DescExtended = Loc.Localize("HelpMainUi_Step20DescExtended", "Still searching for some digital artist " +
            "to help with making these at some point. Until then only the default template exists.");

        public readonly string Step22Title = Loc.Localize("HelpMainUi_Step21Title", "Profile Description");
        public readonly string Step22Desc = Loc.Localize("HelpMainUi_Step21Desc", "More space than the search info provides!");
        public readonly string Step22DescExtended = Loc.Localize("HelpMainUi_Step21DescExtended", "Results can vary based on how the description is calculated.");

        public readonly string Step23Title = Loc.Localize("HelpMainUi_Step22Title", "Previewing Profiles");
        public readonly string Step23Desc = Loc.Localize("HelpMainUi_Step22Desc", "Clicking this button opens a preview of your profile.");

        public readonly string Step24Title = Loc.Localize("HelpMainUi_Step23Title", "Adding/Editing Profile Avatar");
        public readonly string Step24Desc = Loc.Localize("HelpMainUi_Step23Desc", "You can edit your profile image here.");
        public readonly string Step24DescExtended = Loc.Localize("HelpMainUi_Step23DescExtended", "The editor lets you pan, resize, rotate, and zoom uploaded files of any size to the fit you like!");

        public readonly string Step25Title = Loc.Localize("HelpMainUi_Step24Title", "Saving Changes");
        public readonly string Step25Desc = Loc.Localize("HelpMainUi_Step24Desc", "Make sure you save changes, or edits will be lost!");

        public readonly string Step26Title = Loc.Localize("HelpMainUi_Step25Title", "Settings Menu");
        public readonly string Step26Desc = Loc.Localize("HelpMainUi_Step25Desc", "You can access the Settings window by clicking this button, or the gear on the title bar!");
    }

    public class HelpGroups
    {
        public readonly string Step1Title = Loc.Localize("HelpGroups_Step1Title", "Groups Tutorial");
        public readonly string Step1Desc = Loc.Localize("HelpGroups_Step1Desc", "Welcome to the Groups Tutorial! This is a placeholder until the actual tutorial is made.");
    }
    #endregion Tutorials

    public class CoreUi
    {
        public Tabs Tabs { get; set; } = new();
        // Room to add localization for other tab text if needed.
        public Accounts Accounts { get; set; } = new();
    }

    public class Tabs
    {
        public readonly string TabHomepage = Loc.Localize("Tabs_MenuTabHomepage", "Home");
        public readonly string TabWhitelist = Loc.Localize("Tabs_MenuTabWhitelist", "Whitelist");
        public readonly string TabRequests = Loc.Localize("Tabs_MenuTabRequests", "Incoming / Outgoing Requests");
        public readonly string TabRadar = Loc.Localize("Tabs_MenuTabRadar", "Radar Control");
        public readonly string TabChat = Loc.Localize("Tabs_MenuTabChat", "Localized Radar Chat");
        public readonly string TabAccount = Loc.Localize("Tabs_MenuTabAccount", "Account Control");
    } 

    public class Settings
    {
        public readonly string OptionalPlugins = Loc.Localize("Settings_OptionalPlugins", "Plugins:");
        public readonly string PluginValid = Loc.Localize("Settings_PluginValid", "Plugin enabled and up to date.");
        public readonly string PluginInvalid = Loc.Localize("Settings_PluginInvalid", "Plugin is not up to date or Sundouleia has an outdated API.");

        // Outline the tabs for each settings section and the sub-classes of their translations.
        public readonly string TabGeneral = Loc.Localize("Settings_TabsGeneral", "General");
        public readonly string TabPreferences = Loc.Localize("Settings_TabsPreferences", "Preferences");
        public readonly string TabAccounts = Loc.Localize("Settings_TabsAccounts", "My Account");
        public readonly string TabStorage = Loc.Localize("Settings_TabsStorage", "Storage");
        public readonly string TabLogging = Loc.Localize("Settings_TabsLogging", "Logging"); // no sub-class needed.

        public MainOptions MainOptions { get; set; } = new();
        public Preferences Preferences { get; set; } = new();
        public Accounts Accounts { get; set; } = new();
    }

    public class MainOptions
    {
        public readonly string HeaderRadar = Loc.Localize("MainOptions_HeaderRadar", "Radar Preferences");
        public readonly string HeaderUi = Loc.Localize("MainOptions_HeaderUi", "UI Options");

        // Player synchronization options here and stuff.

        public readonly string ShowMainUiOnStartLabel = Loc.Localize("Preferences_ShowMainUiOnStartLabel", "Open the Main Window UI upon plugin startup.");
        public readonly string ShowMainUiOnStartTT = Loc.Localize("Preferences_ShowMainUiOnStartTT", "Determines if the Main UI will open upon plugin startup or not.");

        public readonly string ShowProfilesLabel = Loc.Localize("Preferences_ShowProfilesLabel", "Show Sundouleia profiles on hover");
        public readonly string ShowProfilesTT = Loc.Localize("Preferences_ShowProfilesTT", "Displays the configured user profile after hovering over the player.");

        public readonly string ProfileDelayLabel = Loc.Localize("Preferences_ProfileDelayLabel", "Hover Delay");
        public readonly string ProfileDelayTT = Loc.Localize("Preferences_ProfileDelayTT", "Sets the delay before a profile is displayed on hover.");

    }

    public class Preferences
    {
        // UI Preferences Section
        public readonly string HeaderPairPref = Loc.Localize("Preferences_HeaderPairPref", "Pair Preferences");
        public readonly string HeaderNotifyPref = Loc.Localize("Preferences_HeaderNotifyPref", "Notifications");

        public readonly string ShowVisibleSeparateLabel = Loc.Localize("Preferences_ShowVisibleSeparateLabel", "Show separate Visible group");
        public readonly string ShowVisibleSeparateTT = Loc.Localize("Preferences_ShowVisibleSeparateTT", "Lists paired players within render range in a separate group.");

        public readonly string ShowOfflineSeparateLabel = Loc.Localize("Preferences_ShowOfflineSeparateLabel", "Show separate Offline group");
        public readonly string ShowOfflineSeparateTT = Loc.Localize("Preferences_ShowOfflineSeparateTT", "Lists offline paired players in a separate group.");

        public readonly string PreferNicknamesLabel = Loc.Localize("Preferences_PreferNicknamesLabel", "Prefer nicknames for visible pairs");
        public readonly string PreferNicknamesTT = Loc.Localize("Preferences_PreferNicknamesTT", "Displays nicknames instead of character names for paired players within render range.");

        public readonly string ContextMenusLabel = Loc.Localize("Preferences_ShowContextMenusLabel", "Enable right-click context menu for visible pairs");
        public readonly string ContextMenusTT = Loc.Localize("Preferences_ShowContextMenusTT", "Displays a context menu when right-clicking on a targeted pair." +
            "--SEP--The context menu provides quick access to pair actions or to view a Profile.");

        public readonly string FocusTargetLabel = Loc.Localize("Preferences_FocusTargetLabel", "Use FocusTarget over Target");
        public readonly string FocusTargetTT = Loc.Localize("Preferences_FocusTargetTT", "Uses the FocusTarget instead of the Target for identifying pairs." +
            "--SEP--Used when clicking the eye icon in the whitelist.");

        // Notifications Section
        public readonly string HeaderNotifications = Loc.Localize("Preferences_HeaderNotifications", "Notifications");

        public readonly string ConnectedNotifLabel = Loc.Localize("Preferences_ConnectedNotifLabel", "Enable Connection Notifications");
        public readonly string ConnectedNotifTT = Loc.Localize("Preferences_ConnectedNotifTT", "Displays a notification when server connection status changes." +
            "--SEP--Notifies you when: connected, disconnected, reconnecting or connection lost.");

        public readonly string OnlineNotifLabel = Loc.Localize("Preferences_OnlineNotifLabel", "Enable Online Pair Notifications");
        public readonly string OnlineNotifTT = Loc.Localize("Preferences_OnlineNotifTT", "Displays a notification when a pair comes online.");

        public readonly string LimitForNicksLabel = Loc.Localize("Preferences_LimitForNicksLabel", "Limit Online Pair Notifications to Nicknamed Pairs");
        public readonly string LimitForNicksTT = Loc.Localize("Preferences_LimitForNicksTT", "Limits notifications to pairs with an assigned nickname.");
    }

    public class Accounts
    {
        public readonly string CharaNameLabel = Loc.Localize("Accounts_CharaNameLabel", "Account Character's Name");
        public readonly string CharaWorldLabel = Loc.Localize("Accounts_CharaWorldLabel", "Account Character's World");
        public readonly string CharaKeyLabel = Loc.Localize("Accounts_CharaKeyLabel", "Account Secret Key");

        public readonly string DeleteButtonLabel = Loc.Localize("Accounts_DeleteButtonLabel", "Delete Account");
        public readonly string DeleteButtonDisabledTT = Loc.Localize("Accounts_DeleteButtonDisabledTT", "Cannot delete this account as it is not yet registered.");
        public readonly string DeleteButtonTT = Loc.Localize("Accounts_DeleteButtonTT", "Permanently deleting this account from GagSpeak servers." +
            "--SEP--WARNING: Once an account is deleted, the associated secret key will become unusable." +
            "--SEP--If you wish to create a new account for the currently logged in character, you will need to obtain a new secret key." +
            "--SEP--(A confirmation dialog will open upon clicking this button)");
    }
}
