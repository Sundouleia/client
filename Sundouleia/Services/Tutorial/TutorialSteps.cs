namespace Sundouleia.Services.Tutorial;

public enum TutorialType
{
    MainUi,
    Groups,
}

public enum StepsMainUi
{
    InitialWelcome, // welcome message, warn user to follow tutorial for basic overview, and how to access them at any time (the ? buttons)
    ConnectionState, // Connection Button
    Homepage, // Overview, General shortcut directory of management access.
    Whitelist, // Overview whitelist.
    AddingUsers, // How to Add Pairs, Select dropdown button.
    AttachingMessages, // optional message attachment to requests, close menu on next, and move to account page.
    Requests, // where to see any sent / received requests.
    Radar, // overview of Radar, and what it is.
    RadarUsers, // How they are displayed, if they allow requests (could maybe add if they are in chat but idk)
    RadarChat, // overview of global chat, how it works, log clearing ext.
    RadarChatRules, // where to review rules.
    RadarChatPrivacy, // explain how only last 3 letters of UID is shown.
    ChatEmotes, // how to include emotes.
    ChatScroll, // how to scroll,
    ChatUserExamine, // onHover things (profiles, request sending, ext. Move to account page after, for plug.
    AccountPage, // overview of account page.
    YourUID, // indicate the client's UID, and how this is what others give you to pair.
    ProfileEditing, // on my profile button, open editor on action.
    ProfilePublicity, // what being public -vs- private means.
    SettingTitles, // where titles are shown, how to earn them. (Show WIP)
    CustomizingProfile, // How to unlock customizations. (WIP)
    ProfileDescription, // On click to next step, open light profile, and highlight profile preview.
    ProfilePreview, // On click, close profile preview
    ProfileImageEditor, // On click, close image editor.
    ProfileSaving, // Emphasis on saving changes, and how 'editing without saving' reverts changes.
    ConfigSettings, // the menu way to access settings (mention about the title bar alternative.
}
public enum StepsGroups
{
    Overview, // Dummy placeholder until the Groups Tutorials are actually made.
}