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
    public class Tutorials
    {
        public HelpMainUi MainUi { get; set; } = new();
        public HelpPairGroups PairGroups { get; set; } = new();
        public HelpRadar Radar { get; set; } = new();
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
        public readonly string Step3Desc = Loc.Localize("HelpMainUi_Step3Desc", "Access Sundouleia's many modules here.");
        public readonly string Step3DescExtended = Loc.Localize("HelpMainUi_Step3DescExtended", " ");

        public readonly string Step4Title = Loc.Localize("HelpMainUi_Step4Title", "Whitelist");
        public readonly string Step4Desc = Loc.Localize("HelpMainUi_Step4Desc", "Where your added Users appear.");
        public readonly string Step4DescExtended = Loc.Localize("HelpMainUi_Step4DescExtended",
            "MIDDLE-CLICK => Open this User's Profile.\n" +
            "RIGHT-CLICK => Set a nickname for this User.\n" +
            "Magnify Glass => View the permissions set for you by this User.\n" +
            "Gear => Set your permissions for this User here.\n" +
            "Triple Dots => Interact with this User.");

        public readonly string Step5Title = Loc.Localize("HelpMainUi_Step5Title", "Adding Users");
        public readonly string Step5Desc = Loc.Localize("HelpMainUi_Step5Desc", "Send out User requests here.");
        public readonly string Step5DescExtended = Loc.Localize("HelpMainUi_Step5DescExtended", "Sent requests expire automatically within " +
            "3 days if not responded to, and can also be canceled at anytime.");

        public readonly string Step6Title = Loc.Localize("HelpMainUi_Step6Title", "Attaching Messages");
        public readonly string Step6Desc = Loc.Localize("HelpMainUi_Step6Desc", "Messages can be attached to sent User Requests, if desired.");
        public readonly string Step6DescExtended = Loc.Localize("HelpMainUi_Step6DescExtended", "These can provide context for who's sending the request, helping inform the recipient who you are!");

        public readonly string Step7Title = Loc.Localize("HelpMainUi_Step7Title", "Account Page");
        public readonly string Step7Desc = Loc.Localize("HelpMainUi_Step7Desc", "Manage account settings here.");
        public readonly string Step7DescExtended = Loc.Localize("HelpMainUi_Step7DescExtended", "This page contains important information about you, and access to profile setup, configs, and support links!");

        public readonly string Step8Title = Loc.Localize("HelpMainUi_Step8Title", "Client UID");
        public readonly string Step8Desc = Loc.Localize("HelpMainUi_Step8Desc", "Your UID for pairing.");
        public readonly string Step8DescExtended = Loc.Localize("HelpMainUi_Step8DescExtended", "This defines your account, you shouldn't display this in global chats or kinkplates.");

        public readonly string Step9Title = Loc.Localize("HelpMainUi_Step9Title", "Safewords");
        public readonly string Step9Desc = Loc.Localize("HelpMainUi_Step9Desc", "Triggered with [/safeword YOURSAFEWORD], or [/safeword YOURSAFEWORD SPECIFICUID]. This removes everything from you!");
        public readonly string Step9DescExtended = Loc.Localize("HelpMainUi_Step9DescExtended", "Using your safeword will override everything, so please use it responsibly. " +
            "If you get stuck and can't use your chat, you can use the hardcore safeword with CTRL+ALT+Backspace, which will disable all hardcore settings across all pairs.");

        public readonly string Step10Title = Loc.Localize("HelpMainUi_Step10Title", "Setting Safeword");
        public readonly string Step10Desc = Loc.Localize("HelpMainUi_Step10Desc", "Press this stencil to set your personal Safeword.");
        public readonly string Step10DescExtended = Loc.Localize("HelpMainUi_Step10DescExtended", "Safewords have a 5 minute cooldown when used, and will remove all active bindings.");

        public readonly string Step11Title = Loc.Localize("HelpMainUi_Step11Title", "Profile™ Editing");
        public readonly string Step11Desc = Loc.Localize("HelpMainUi_Step11Desc", "Make Customizations to your Profile™ here.");
        public readonly string Step11DescExtended = Loc.Localize("HelpMainUi_Step11DescExtended", "You can customize the display of your Profile™, description, and Avatar here.");

        public readonly string Step12Title = Loc.Localize("HelpMainUi_Step12Title", "Profile™ Publicity");
        public readonly string Step12Desc = Loc.Localize("HelpMainUi_Step12Desc", "If a Profile™ is public, it can be viewed in Global Chat.");
        public readonly string Step12DescExtended = Loc.Localize("HelpMainUi_Step12DescExtended", "Private Profiles™ can only be viewed by yourself and your User pairs.");

        public readonly string Step13Title = Loc.Localize("HelpMainUi_Step13Title", "Profile™ Titles");
        public readonly string Step13Desc = Loc.Localize("HelpMainUi_Step13Desc", "Earned through Achievements, and displayed on your Profile™.");
        public readonly string Step13DescExtended = Loc.Localize("HelpMainUi_Step13DescExtended", "Over 200 different titles exist, and are shown in Light and Full Profile™'s");

        public readonly string Step14Title = Loc.Localize("HelpMainUi_Step14Title", "Customizing Profile");
        public readonly string Step14Desc = Loc.Localize("HelpMainUi_Step14Desc", "Unlocked as rewards from Achievements! (WIP) (SEE INFO)");
        public readonly string Step14DescExtended = Loc.Localize("HelpMainUi_Step14DescExtended", "Profile™ customizations are still (WIP) as I have no graphic artist support, " +
            "if you would like to contribute, please let me know! Until then, this is WIP.");

        public readonly string Step15Title = Loc.Localize("HelpMainUi_Step15Title", "Profile Description");
        public readonly string Step15Desc = Loc.Localize("HelpMainUi_Step15Desc", "More space than the search info provides!");
        public readonly string Step15DescExtended = Loc.Localize("HelpMainUi_Step15DescExtended", "Results can vary based on how the description is calculated, " +
            "preview result on light and full kinkplates!");

        public readonly string Step16Title = Loc.Localize("HelpMainUi_Step16Title", "Previewing Light Profile™");
        public readonly string Step16Desc = Loc.Localize("HelpMainUi_Step16Desc", "Your light Profile™ can be previewed here.");
        public readonly string Step16DescExtended = Loc.Localize("HelpMainUi_Step16DescExtended", "Light Kinkplates only display profile image, supporter tier, titles, and descriptions.");

        public readonly string Step17Title = Loc.Localize("HelpMainUi_Step17Title", "Previewing Full Profile™");
        public readonly string Step17Desc = Loc.Localize("HelpMainUi_Step17Desc", "Your full Profile™ can be previewed here.");
        public readonly string Step17DescExtended = Loc.Localize("HelpMainUi_Step17DescExtended", "Full Kinkplates can reflect your current restrictions, hardcore traits, and hardcore states!");

        public readonly string Step18Title = Loc.Localize("HelpMainUi_Step18Title", "Editing Profile Image");
        public readonly string Step18Desc = Loc.Localize("HelpMainUi_Step18Desc", "You can edit your profile image here.");
        public readonly string Step18DescExtended = Loc.Localize("HelpMainUi_Step18DescExtended", "The editor lets you pan, resize, rotate, and zoom uploaded files of any size to the fit you like!");

        public readonly string Step19Title = Loc.Localize("HelpMainUi_Step19Title", "Saving Profile Changes");
        public readonly string Step19Desc = Loc.Localize("HelpMainUi_Step19Desc", "Make sure you save changes, or edits will be lost!");
        public readonly string Step19DescExtended = Loc.Localize("HelpMainUi_Step19DescExtended", " ");

        public readonly string Step20Title = Loc.Localize("HelpMainUi_Step20Title", "Sundouleia Settings Menu");
        public readonly string Step20Desc = Loc.Localize("HelpMainUi_Step20Desc", "You can access the Settings window by clicking this button.");
        public readonly string Step20DescExtended = Loc.Localize("HelpMainUi_Step20DescExtended", "You can also access it from any of the tabs by pressing the cog in the top bar.");

        // public readonly string Step21Title = Loc.Localize("HelpMainUi_Step21Title", "Title Bar Sundouleia Settings");
        // public readonly string Step21Desc = Loc.Localize("HelpMainUi_Step21Desc", "You can also access them from the title bar.");
        // public readonly string Step21DescExtended = Loc.Localize("HelpMainUi_Step21DescExtended", " ");

        public readonly string Step21Title = Loc.Localize("HelpMainUi_Step22Title", "Pattern Hub");
        public readonly string Step21Desc = Loc.Localize("HelpMainUi_Step22Desc", "Browse and explore patterns uploaded by others.");
        public readonly string Step21DescExtended = Loc.Localize("HelpMainUi_Step22DescExtended", " ");

        public readonly string Step22Title = Loc.Localize("HelpMainUi_Step23Title", "Pattern Search");
        public readonly string Step22Desc = Loc.Localize("HelpMainUi_Step23Desc", "Use tags and filters to narrow your search results.");
        public readonly string Step22DescExtended = Loc.Localize("HelpMainUi_Step23DescExtended", "Up to a maximum of 50 results are polled, so if " +
            "you can't find the result you are looking for, narrow it with filters!");

        public readonly string Step23Title = Loc.Localize("HelpMainUi_Step24Title", "Pattern Results");
        public readonly string Step23Desc = Loc.Localize("HelpMainUi_Step24Desc", "Results let you preview devices & motors used, duration, and authors.");
        public readonly string Step23DescExtended = Loc.Localize("HelpMainUi_Step24DescExtended", " ");

        public readonly string Step24Title = Loc.Localize("HelpMainUi_Step25Title", "Moodle Hub");
        public readonly string Step24Desc = Loc.Localize("HelpMainUi_Step25Desc", "Browse and explore Moodles uploaded by others.");
        public readonly string Step24DescExtended = Loc.Localize("HelpMainUi_Step25DescExtended", " ");

        public readonly string Step25Title = Loc.Localize("HelpMainUi_Step26Title", "Moodle Search");
        public readonly string Step25Desc = Loc.Localize("HelpMainUi_Step26Desc", "Use tags and filters to narrow your search results.");
        public readonly string Step25DescExtended = Loc.Localize("HelpMainUi_Step26DescExtended", "Up to a maximum of 75 results are polled, so if " +
            "you can't find the result you are looking for, narrow it with filters!");

        public readonly string Step26Title = Loc.Localize("HelpMainUi_Step27Title", "Moodle Results");
        public readonly string Step26Desc = Loc.Localize("HelpMainUi_Step27Desc", "You can preview a Moodle by hovering over it's icon.");
        public readonly string Step26DescExtended = Loc.Localize("HelpMainUi_Step27DescExtended", "You can also try on, like, or grab a copy of the Moodle for yourself.");

        public readonly string Step27Title = Loc.Localize("HelpMainUi_Step28Title", "Global Chat");
        public readonly string Step27Desc = Loc.Localize("HelpMainUi_Step28Desc", "Chat Anonymously with other Users from anywhere in the world with Global Chat!");
        public readonly string Step27DescExtended = Loc.Localize("HelpMainUi_Step28DescExtended", "ChatLogs are restored on reconnection, and reset at midnight every day relative to your local time zone.");

        public readonly string Step28Title = Loc.Localize("HelpMainUi_Step29Title", "Using Global Chat");
        public readonly string Step28Desc = Loc.Localize("HelpMainUi_Step29Desc", "To talk in Global Chat, you must verify your account first! This is to prevent anonymous harassment.");
        public readonly string Step28DescExtended = Loc.Localize("HelpMainUi_Step29DescExtended", "In order to verify your account, you will need to join the discord server, where further instructions can be found.");

        public readonly string Step29Title = Loc.Localize("HelpMainUi_Step30Title", "Chat Emotes");
        public readonly string Step29Desc = Loc.Localize("HelpMainUi_Step30Desc", "You can add expressive emotes to messages!");
        public readonly string Step29DescExtended = Loc.Localize("HelpMainUi_Step30DescExtended", "Emotes can also be manually added to chat messages by typing out emotes like discord emotes. :catsnuggle:");

        public readonly string Step30Title = Loc.Localize("HelpMainUi_Step31Title", "Chat Scroll");
        public readonly string Step30Desc = Loc.Localize("HelpMainUi_Step31Desc", "Sets if the window will always auto-scroll to the last sent message.");
        public readonly string Step30DescExtended = Loc.Localize("HelpMainUi_Step31DescExtended", "Turning Auto-Scroll off lets you scroll up freely.");

        public readonly string Step31Title = Loc.Localize("HelpMainUi_Step32Title", "Chat Message Examine");
        public readonly string Step31Desc = Loc.Localize("HelpMainUi_Step32Desc", "Hover messages to see when they were sent, the User's Light Profile™, or send them a request!");
        public readonly string Step31DescExtended = Loc.Localize("HelpMainUi_Step32DescExtended", "Additionally, you are able to choose to add a kinkster to " +
            "your silence list, hiding messages from them until the next plugin restart.");

        public readonly string Step32Title = Loc.Localize("HelpMainUi_Step33Title", "Self Plug");
        public readonly string Step32Desc = Loc.Localize("HelpMainUi_Step33Desc", "If you ever fancy tossing a tip or becoming a supporter as a thanks for all the hard work, or just to help support me, it would be much appreciated." +
            "\n\nBut please don't feel guilty if you don't. Only support me if you want to! I will always love and cherish you all regardless ♥");
        public readonly string Step32DescExtended = Loc.Localize("HelpMainUi_Step33DescExtended", " ");
    }

    public class HelpPairGroups
    {

    }


    public class HelpRadar
    {

    }

    #endregion Tutorials

    #region CoreUi
    public class CoreUi
    {
        public Tabs Tabs { get; set; } = new();
        public Homepage Homepage { get; set; } = new();
        public Whitelist Whitelist { get; set; } = new();
        public Radar Radar { get; set; } = new();
        public RadarChat RadarChat { get; set; } = new();
    }

    public class Tabs
    {
        public readonly string MenuTabHomepage = Loc.Localize("Tabs_MenuTabHomepage", "Home");
        public readonly string MenuTabWhitelist = Loc.Localize("Tabs_MenuTabWhitelist", "Pair Whitelist");
        public readonly string MenuTabDiscover = Loc.Localize("Tabs_MenuTabDiscover", "Pattern Hub");
        public readonly string MenuTabGlobalChat = Loc.Localize("Tabs_MenuTabGlobalChat", "Global Cross-Region Chat");
        public readonly string MenuTabAccount = Loc.Localize("Tabs_MenuTabAccount", "Account Settings");

        public readonly string ToyboxOverview = Loc.Localize("Tabs_ToyboxOverview", "Overview");
        public readonly string ToyboxVibeServer = Loc.Localize("Tabs_ToyboxVibeServer", "Vibe Server");
        public readonly string Patterns = Loc.Localize("Tabs_Patterns", "Patterns");
        public readonly string ToyboxTriggers = Loc.Localize("Tabs_ToyboxTriggers", "Triggers");
        public readonly string ToyboxAlarms = Loc.Localize("Tabs_ToyboxAlarms", "Alarms");

        public readonly string AchievementsComponentGeneral = Loc.Localize("Tabs_AchievementsComponentGeneral", "General");
        public readonly string AchievementsComponentOrders = Loc.Localize("Tabs_AchievementsComponentOrders", "Orders");
        public readonly string AchievementsComponentGags = Loc.Localize("Tabs_AchievementsComponentGags", "Gags");
        public readonly string AchievementsComponentWardrobe = Loc.Localize("Tabs_AchievementsComponentWardrobe", "Wardrobe");
        public readonly string AchievementsComponentPuppeteer = Loc.Localize("Tabs_AchievementsComponentPuppeteer", "Puppeteer");
        public readonly string AchievementsComponentToybox = Loc.Localize("Tabs_AchievementsComponentToybox", "Toybox");
        public readonly string AchievementsComponentsHardcore = Loc.Localize("Tabs_AchievementsComponentsHardcore", "Hardcore");
        public readonly string AchievementsComponentRemotes = Loc.Localize("Tabs_AchievementsComponentRemotes", "Sex Toy Remote");
        public readonly string AchievementsComponentSecrets = Loc.Localize("Tabs_AchievementsComponentSecrets", "Hidden");
    }

    public class Homepage
    {
        // Add more here if people actually care for it.
    }

    public class Whitelist
    {
        // Add more here if people actually care for it.
    }

    public class Radar
    {
        // Add more here if people actually care for it.
    }

    public class RadarChat
    {
        // Add more here if people actually care for it.
    }

    #endregion CoreUi

    #region Settings
    public class Settings
    {
        public readonly string OptionalPlugins = Loc.Localize("Settings_OptionalPlugins", "Plugins:");
        public readonly string PluginValid = Loc.Localize("Settings_PluginValid", "Plugin enabled and up to date.");
        public readonly string PluginInvalid = Loc.Localize("Settings_PluginInvalid", "Plugin is not up to date or Sundouleia has an outdated API.");

        public readonly string AccountClaimText = Loc.Localize("Settings_AccountClaimText", "Register account:");

        // Can probably spread this out better.
        public readonly string TabsGlobal = Loc.Localize("Settings_TabsGlobal", "General");
        public readonly string TabsPreferences = Loc.Localize("Settings_TabsPreferences", "Chat & UI");
        public readonly string TabsAccounts = Loc.Localize("Settings_TabsAccounts", "Account Management");

        public MainOptions MainOptions { get; set; } = new();
        public Preferences Preferences { get; set; } = new();
        public Accounts Accounts { get; set; } = new();
    }

    public class MainOptions
    {
        public readonly string HeaderDefaults = Loc.Localize("MainOptions_HeaderDefaults", "Default Preferences");

        // Player syncronization options here and stuff.
    }

    public class Preferences
    {
        // UI Preferences Section
        public readonly string HeaderUiPrefs = Loc.Localize("Preferences_HeaderUiPrefs", "User Interface");

        public readonly string ShowMainUiOnStartLabel = Loc.Localize("Preferences_ShowMainUiOnStartLabel", "Open the Main Window UI upon plugin startup.");
        public readonly string ShowMainUiOnStartTT = Loc.Localize("Preferences_ShowMainUiOnStartTT", "Determines if the Main UI will open upon plugin startup or not.");

        public readonly string RadarDtrLabel = Loc.Localize("Preferences_RadarDtrEntryLabel", "Display DTR Entry for Radar Info.");
        public readonly string RadarDtrTT = Loc.Localize("Preferences_RadarDtrEntryTT", "Display DTR Entry of how many Sundouleia Users are present in your current territory!");

        public readonly string ShowVisibleSeparateLabel = Loc.Localize("Preferences_ShowVisibleSeparateLabel", "Show separate Visible group");
        public readonly string ShowVisibleSeparateTT = Loc.Localize("Preferences_ShowVisibleSeparateTT", "Lists paired players within render range in a separate group.");

        public readonly string ShowOfflineSeparateLabel = Loc.Localize("Preferences_ShowOfflineSeparateLabel", "Show separate Offline group");
        public readonly string ShowOfflineSeparateTT = Loc.Localize("Preferences_ShowOfflineSeparateTT", "Lists offline paired players in a separate group.");

        public readonly string PreferNicknamesLabel = Loc.Localize("Preferences_PreferNicknamesLabel", "Prefer nicknames for visible pairs");
        public readonly string PreferNicknamesTT = Loc.Localize("Preferences_PreferNicknamesTT", "Displays nicknames instead of character names for paired players within render range.");

        public readonly string ShowProfilesLabel = Loc.Localize("Preferences_ShowProfilesLabel", "Show Sundouleia profiles on hover");
        public readonly string ShowProfilesTT = Loc.Localize("Preferences_ShowProfilesTT", "Displays the configured user profile after hovering over the player.");

        public readonly string ProfileDelayLabel = Loc.Localize("Preferences_ProfileDelayLabel", "Hover Delay");
        public readonly string ProfileDelayTT = Loc.Localize("Preferences_ProfileDelayTT", "Sets the delay before a profile is displayed on hover.");

        public readonly string ContextMenusLabel = Loc.Localize("Preferences_ShowContextMenusLabel", "Enable right-click context menu for visible pairs");
        public readonly string ContextMenusTT = Loc.Localize("Preferences_ShowContextMenusTT", "Displays a context menu when right-clicking on a targeted pair." +
            "--SEP--The context menu provides quick access to pair actions or to view a Profile.");

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
        public readonly string PrimaryLabel = Loc.Localize("Accounts_PrimaryLabel", "Primary Account");
        public readonly string SecondaryLabel = Loc.Localize("Accounts_SecondaryLabel", "Secondary Accounts");
        public readonly string NoSecondaries = Loc.Localize("Accounts_NoSecondaries", "No secondary accounts to display." +
            "\nA secondary account key can be obtained by registering with the bot in the Sundouleia Discord server.");

        public readonly string CharaNameLabel = Loc.Localize("Accounts_CharaNameLabel", "Account Character's Name");
        public readonly string CharaWorldLabel = Loc.Localize("Accounts_CharaWorldLabel", "Account Character's World");
        public readonly string CharaKeyLabel = Loc.Localize("Accounts_CharaKeyLabel", "Account Secret Key");

        public readonly string DeleteButtonLabel = Loc.Localize("Accounts_DeleteButtonLabel", "Delete Account");
        public readonly string DeleteButtonDisabledTT = Loc.Localize("Accounts_DeleteButtonDisabledTT", "Cannot delete this account as it is not yet registered.");
        public readonly string DeleteButtonTT = Loc.Localize("Accounts_DeleteButtonTT", "Permanently deleting this account from Sundouleia servers." +
            "--SEP--WARNING: Once an account is deleted, the associated secret key will become unusable." +
            "--SEP--If you wish to create a new account for the currently logged in character, you will need to obtain a new secret key." +
            "--SEP--(A confirmation dialog will open upon clicking this button)");
        public readonly string DeleteButtonPrimaryTT = Loc.Localize("Accounts_DeleteButtonPrimaryTT", "--SEP----COL--DELETING THIS ACCOUNT WILL ALSO DELETE ALL SECONDARY ACCOUNTS");

        public readonly string FingerprintPrimary = Loc.Localize("Accounts_FingerprintPrimary", "Primary Sundouleia Account");
        public readonly string FingerprintSecondary = Loc.Localize("Accounts_FingerprintSecondary", "Secondary Sundouleia Account");

        public readonly string SuccessfulConnection = Loc.Localize("Accounts_SuccessfulConnection", "Successfully connected to the Sundouleia servers with a registered secret key." +
            "--SEP--This secret key is bound to this character and cannot be removed unless the account is deleted.");
        public readonly string NoSuccessfulConnection = Loc.Localize("Accounts_NoSuccessfulConnection", "Failed to connect to the Sundouleia servers with a registered secret key.");
        public readonly string EditKeyAllowed = Loc.Localize("Accounts_EditKeyAllowed", "Toggle display of secret key field");
        public readonly string EditKeyNotAllowed = Loc.Localize("Accounts_EditKeyNotAllowed", "Cannot change a secret key that has been verified. This character is now bound to this account.");
        public readonly string CopyKeyToClipboard = Loc.Localize("Accounts_CopyKeyToClipboard", "Click to copy secret key to clipboard");

        public readonly string RemoveAccountPrimaryWarning = Loc.Localize("Accounts_RemoveAccountPrimaryWarning", "By deleting your primary account, all secondary accounts will also be deleted.");
        public readonly string RemoveAccountWarning = Loc.Localize("Accounts_RemoveAccountWarning", "Your UID will be removed from all pairing lists.\nYou will be unable to use this secret key.");
        public readonly string RemoveAccountConfirm = Loc.Localize("Accounts_RemoveAccountConfirm", "Are you sure you want to delete this account?");
    }
}
