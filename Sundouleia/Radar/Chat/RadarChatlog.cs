using CkCommons;
using CkCommons.Chat;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.RichText;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using OtterGui.Text;
using Sundouleia.Gui;
using Sundouleia.Gui.Components;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.Services.Tutorial;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;
using System.Globalization;

namespace Sundouleia.Radar.Chat;

// Maybe remove chatId from here, idk.
public record SundChatMessage(UserData Sender, string Name, string Message)
    : CkChatMessage(Name, Message, DateTime.UtcNow)
{
    public override string UID => Sender.UID ?? base.UID;
    public CkVanityTier Tier => Sender.Tier;
    public uint Color => Sender.Color;
    public uint Glow => Sender.GlowColor;
}

public record RadarChatMessage(string MsgId, UserData Sender, string Name, string Message, DateTime TimestampUTC)
    : CkChatMessage(Name, Message, TimestampUTC)
{
    public override string UID => Sender.UID ?? base.UID;
    public CkVanityTier Tier => Sender.Tier;
    public uint Color => Sender.Color;
    public uint Glow => Sender.GlowColor;
}

// Revised radar chat should draw with its own IDs and formatting scales.
public class RadarChatLog : CkChatlog<RadarChatMessage>, IMediatorSubscriber, IDisposable
{
    private static RichTextFilter AllowedTypes = RichTextFilter.Emotes;

    private readonly ILogger<RadarChatLog> _logger;
    private readonly MainHub _hub;
    private readonly MainMenuTabs _tabMenu;
    private readonly SundesmoManager _sundesmos;
    private readonly TutorialService _guides;
    public SundouleiaMediator Mediator { get; }

    // Private variables that are used by internal methods.
    private bool _welcomeLogShown = false;
    private bool _emoteSelectionOpened = false;

    // TODO: Remove the base label, and enforce an ID to be passed in when drawing for uniqueness instead.
    public RadarChatLog(ILogger<RadarChatLog> logger, SundouleiaMediator mediator, MainHub hub,
        MainMenuTabs tabs, SundesmoManager sundesmos, TutorialService guides) 
        : base(0, "Radar Chat", 500)
    {
        _logger = logger;
        Mediator = mediator;
        _hub = hub;
        _tabMenu = tabs;
        _sundesmos = sundesmos;
        _guides = guides;

        // Have calls here to load in the chat log history or clear it or and what not.
        Mediator.Subscribe<NewRadarChatMessage>(this, msg => AddNetworkMessage(msg.Message));
    }

    public static bool AccessBlocked => ChatBlocked || NotVerified;
    public static bool ChatBlocked => !MainHub.Reputation.ChatUsage;
    public static bool NotVerified => !MainHub.Reputation.IsVerified;
    public static bool NewCorbyMsg { get; private set; } = false;
    public static int NewMsgCount { get; private set; } = 0;

    void IDisposable.Dispose()
    {
        Mediator.UnsubscribeAll(this);
        GC.SuppressFinalize(this);
    }

    public void CreateOrReinitChatlog(List<LoggedRadarChatMessage> initialMessages)
    {
        // If there are currently any messages, clear them out.
        if (Messages.Count() > 0)
        {
            Messages.Clear();
            unreadSinceScroll = 0;
        }

        // Append all initial messages
        foreach (var msg in initialMessages)
        {
            // Do the internal message handling voodoo here (Do later)
            // Maybe have some internal helper function for message parse conversion.
            // Messages.PushBack(msg);
        }

        // If we have now shown the welcome message, do that.
        if (!_welcomeLogShown)
        {
            try
            {
                AddMessage(new(Guid.NewGuid().ToString("N"), new("System"), "System",
                    $"[color=grey2]Welcome to {LocationSvc.Current.TerritoryName}'s Radar Chat! Your Name displays as " +
                    $"[color=yellow]{MainHub.OwnUserData.AnonName}[/color] to others! Feel free to say hi![/color][line]",
                    DateTime.UtcNow));
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to add default welcome message: {ex}");
            }
        }
    }

    // Maybe edit later
    public void SetDisabledStates(bool content, bool input)
    {
        disableContent = content;
        disableInput = input;
    }

    // Maybe edit later
    public void SetAutoScroll (bool newState)
        => DoAutoScroll = newState;

    // Maybe edit later
    protected override string ToTooltip(RadarChatMessage message)
        => $"Sent @ {message.Timestamp.ToString("T", CultureInfo.CurrentCulture)}" +
        "--NL----COL--[Middle-Click]--COL-- Open Profile";

    // Perform logic for a received RadarChatMessage
    private void AddNetworkMessage(LoggedRadarChatMessage loggedMsg)
    {
        // 1) Filter out blocked users here. (Do this server level? Not sure really.
        // Might be more benificial to cache clientside.

        var fromSelf = loggedMsg.Sender.UID == MainHub.UID;

        // 2) Update the chat contents. (find some other way to do this later)
        // Can also use logic here to handle stuff like mentions, and other notions.
        if (_tabMenu.TabSelection is not MainMenuTabs.SelectedTab.RadarChat)
            NewMsgCount++;
        else
        {
            NewMsgCount = 0;
            NewCorbyMsg = false;
        }

        var finalName = loggedMsg.Sender.AnonName; // "Anon.User-XXXX"
        // 4) Adjust sender name based on special conditions.
        if (loggedMsg.Sender.Tier is CkVanityTier.ShopKeeper)
            finalName = $"Cordy";
        else if (_sundesmos.GetUserOrDefault(loggedMsg.Sender) is { } sundesmo)
            finalName = $"{sundesmo.GetNickAliasOrUid()} ({loggedMsg.Sender.UID[..4]})";
        else if (fromSelf)
            finalName = $"{loggedMsg.Sender.DisplayName} ({loggedMsg.Sender.UID[..4]})";
        
        // Construct the network message to the RadarCkChatMessage format.
        AddMessage(new RadarChatMessage(loggedMsg.MsgId, loggedMsg.Sender, finalName, loggedMsg.Message, loggedMsg.TimeSentUTC));
    }

    // Fix this later!
    protected override void AddMessage(RadarChatMessage newMsg)
    {
        // Cordy is special girl :3
        if (newMsg.Tier is CkVanityTier.ShopKeeper)
        {
            // Apply Cordy's signature color to her name, and also prefix her icon and give all RichText permissions.
            UserColors[newMsg.UID] = SundCol.ShopKeeperColor.Vec4();
            var prefix = $"[img=RequiredImages\\Tier4Icon][rawcolor={SundCol.ShopKeeperColor.Uint()}]{newMsg.Name}[/rawcolor]: ";
            Messages.PushBack(newMsg with { Message = $"{prefix} [rawcolor={SundCol.ShopKeeperText.Uint()}]{newMsg.Message}[/rawcolor]" });
            unreadSinceScroll++;
            NewCorbyMsg = true;
        }
        // System messages should not inc the unread count.
        else if (string.Equals(newMsg.UID, "System", StringComparison.OrdinalIgnoreCase))
        {
            // System messages are special, they are not colored.
            var prefix = $"[rawcolor=0xFF0000FF]{newMsg.Name}[/rawcolor]: ";
            Messages.PushBack(newMsg with { Message = prefix + newMsg.Message });
        }
        // Other messages should be sanitized.
        else
        {
            // Assign color
            var col = ColorHelpers.RgbaVector4ToUint(AssignSenderColor(newMsg));
            // strip away CkRichText formatters not permitted. (Prevent chat chaos)
            var sanitizedMsg = CkRichText.StripDisallowedRichTags(newMsg.Message, AllowedTypes);
            // Append prefix formatting to supporters.
            var prefix = newMsg.Tier switch
            {
                CkVanityTier.DistinguishedConnoisseur => $"[img=RequiredImages\\Tier3Icon][rawcolor={col}]{newMsg.Name}[/rawcolor]: ",
                CkVanityTier.EsteemedPatron => $"[img=RequiredImages\\Tier2Icon][rawcolor={col}]{newMsg.Name}[/rawcolor]: ",
                CkVanityTier.ServerBooster => $"[img=RequiredImages\\TierBoosterIcon][rawcolor={col}]{newMsg.Name}[/rawcolor]: ",
                CkVanityTier.IllustriousSupporter => $"[img=RequiredImages\\Tier1Icon][rawcolor={col}]{newMsg.Name}[/rawcolor]: ",
                _ => $"[rawcolor={col}]{newMsg.Name}[/rawcolor]: "
            };
            Messages.PushBack(newMsg with { Message = prefix + sanitizedMsg });
            unreadSinceScroll++;
        }
    }

    public override void DrawChatInputRow()
    {
        using var _ = ImRaii.Group();

        var scrollIcon = DoAutoScroll ? FAI.ArrowDownUpLock : FAI.ArrowDownUpAcrossLine;
        var width = ImGui.GetContentRegionAvail().X;

        // Set keyboard focus to the chat input box if needed
        if (shouldFocusChatInput)// && ImGui.IsWindowFocused())
        {
            Svc.Logger.Information("Setting keyboard focus to chat input box.", LoggerType.RadarChat);
            ImGui.SetKeyboardFocusHere(0);
            shouldFocusChatInput = false;
        }
        ImGui.SetNextItemWidth(width - (CkGui.IconButtonSize(scrollIcon).X + ImGui.GetStyle().ItemInnerSpacing.X) * 3);
        using (ImRaii.Disabled(disableInput))
        {
            ImGui.InputTextWithHint($"##ChatInput{Label}{ID}", "type here...", ref previewMessage, 300);
        }

        // Process submission Prevent losing chat focus after pressing the Enter key.
        if (!disableInput && ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            shouldFocusChatInput = true;
            _emoteSelectionOpened = false;
            OnSendMessage(previewMessage);
        }

        // toggle emote viewing.
        ImUtf8.SameLineInner();
        using (ImRaii.PushColor(ImGuiCol.Text, SundCol.Gold.Uint(), _emoteSelectionOpened))
        {
            if (CkGui.IconButton(FAI.Heart, disabled: disableInput))
                _emoteSelectionOpened = !_emoteSelectionOpened;
        }
        CkGui.AttachTooltip($"Toggles Quick-Emote selection.", disableInput);
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ChatEmotes, MainUI.LastPos, MainUI.LastSize);

        // Toggle AutoScroll functionality
        ImUtf8.SameLineInner();
        if (CkGui.IconButton(scrollIcon, disabled: disableInput))
            DoAutoScroll = !DoAutoScroll;
        CkGui.AttachTooltip($"Toggles AutoScroll (Current: {(DoAutoScroll ? "Enabled" : "Disabled")})");
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ChatScroll, MainUI.LastPos, MainUI.LastSize);

        // draw the popout button
        ImUtf8.SameLineInner();
        if (CkGui.IconButton(FAI.Expand, disabled: disableInput || !ImGui.GetIO().KeyShift))
            Mediator.Publish(new UiToggleMessage(typeof(RadarChatPopoutUI)));
        CkGui.AttachTooltip("Open a Popout of the Radar Chat!--SEP--Hold SHIFT to activate!");
    }

    protected override void DrawPostChatLog(Vector2 inputPosMin)
    {
        // Preview Text padding area
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(5));
        var drawTextPreview = !string.IsNullOrWhiteSpace(previewMessage);
        // if we should show the preview, do so.
        if (drawTextPreview)
            DrawTextPreview(previewMessage, inputPosMin);

        // Afterwards, we need to make sure that we can create a new window for the emotes at the correct space if so.
        if (_emoteSelectionOpened)
        {
            var drawPos = drawTextPreview ? ImGui.GetItemRectMin() : inputPosMin;
            DrawQuickEmoteWindow(drawPos);
        }
    }

    private void DrawQuickEmoteWindow(Vector2 drawPos)
    {
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemInnerSpacing;
        var emoteCache = CosmeticService.EmoteTextures.Cache;
        var totalEmotes = emoteCache.Count;
        var emoteSize = new Vector2(ImGui.GetFrameHeightWithSpacing());
        var emotesPerRow = Math.Max(1, (int)((totalWidth.RemoveWinPadX() + spacing.X) / (emoteSize.X + spacing.X)));
        var rows = (int)Math.Ceiling((float)totalEmotes / emotesPerRow);
        var winHeight = (emoteSize.Y + spacing.Y) * Math.Clamp(rows, 1, 2) + spacing.Y;


        var winPos = drawPos - new Vector2(0, winHeight.AddWinPadY() + spacing.Y);
        ImGui.SetNextWindowPos(winPos);
        using var c = CkRaii.ChildPaddedW("Quick-Emote-View", totalWidth, winHeight, wFlags: WFlags.AlwaysVerticalScrollbar);

        var wdl = ImGui.GetWindowDrawList();
        wdl.PushClipRect(winPos, winPos + c.InnerRegion.WithWinPadding(), false);
        wdl.AddRectFilled(winPos, winPos + c.InnerRegion.WithWinPadding(), 0xCC000000, 5, ImDrawFlags.RoundCornersAll);
        wdl.AddRect(winPos, winPos + c.InnerRegion.WithWinPadding(), ImGuiColors.ParsedGold.ToUint(), 5, ImDrawFlags.RoundCornersAll);
        wdl.PopClipRect();

        var count = 0;
        foreach (var (key, wrap) in CosmeticService.EmoteTextures.Cache)
        {
            ImGui.Dummy(emoteSize);
            var min = ImGui.GetItemRectMin();
            wdl.AddDalamudImageRounded(wrap, min, emoteSize, 5, key.ToRichTextString());
            // if clicked, append the string to our message.
            if (ImGui.IsItemClicked())
            {
                previewMessage += $"{key.ToRichTextString()} ";
                shouldFocusChatInput = true;
            }

            count++;
            if (count % emotesPerRow != 0)
                ImUtf8.SameLineInner();
        }
    }

    protected override void OnMiddleClick(RadarChatMessage message)
    {
        if (disableContent) return;
        if (message.UID == "System") return;
        Mediator.Publish(new ProfileOpenMessage(message.Sender));
    }

    protected override void OnSendMessage(string message)
    {
        shouldFocusChatInput = true;
        if (string.IsNullOrWhiteSpace(previewMessage))
            return;
        // Send message to the server (Ensure we pull our flags here properly and such
        _hub.RadarSendChat(new SentRadarMessage(MainHub.OwnUserData, previewMessage, RadarChatFlags.None)).ConfigureAwait(false);
        // Clear preview
        previewMessage = string.Empty;
    }

    protected override void DrawPopupInternal()
    {
        if (LastInteractedMsg is null)
            return;

        var shiftHeld = ImGui.GetIO().KeyShift;
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        var isSystemMsg = LastInteractedMsg.UID == "System";
        var isOwnMsg = LastInteractedMsg.UID == MainHub.UID;
        var disableSilence = !ctrlHeld || isSystemMsg || isOwnMsg;
        var disableBlock = !(ctrlHeld && shiftHeld) || isSystemMsg || isOwnMsg;

        // Profile Viewing
        CkGui.FontText(LastInteractedMsg.Name, Svc.PluginInterface.UiBuilder.MonoFontHandle, ImGuiColors.ParsedGold);
        ImGui.Separator();

        // Temp Silencing.
        using (ImRaii.Disabled(disableSilence))
            if (ImGui.Selectable("Hide Messages from User") && !isOwnMsg && !isSystemMsg)
            {
                SilenceList.Add(LastInteractedMsg.UID);
                ClosePopupAndResetMsg();
                return;
            }
        CkGui.AttachTooltip(ctrlHeld ? $"Hides messages from {LastInteractedMsg.Name} until plugin reload/restart." : "Must be holding CTRL to select.");

        // Permanent Blocking.
        using (ImRaii.Disabled(disableBlock))
            if (ImGui.Selectable("Block User") && !isOwnMsg && !isSystemMsg)
            {
                UiService.SetUITask(async () => await _hub.UserBlock(new(LastInteractedMsg.Sender)));
                ClosePopupAndResetMsg();
                return;
            }
        CkGui.AttachTooltip(shiftHeld ? $"Blocks {LastInteractedMsg.Name} permanently. (Currently not implemented)" : "Must be holding CTRL+SHIFT to select.");

        // Chat Reporting
        using (ImRaii.Disabled(disableBlock))
            if (ImGui.Selectable("Report Chat Behavior") && !isOwnMsg && !isSystemMsg)
            {
                Mediator.Publish(new OpenReportUIMessage(LastInteractedMsg!.Sender, ReportKind.Chat));
                ClosePopupAndResetMsg();
                return;
            }
        CkGui.AttachTooltip(shiftHeld ? $"Report {LastInteractedMsg!.Name}'s chat behavior." : "Must be holding CTRL+SHIFT to select.");
    }

    private void ClosePopupAndResetMsg()
    {
        LastInteractedMsg = null;
        ImGui.CloseCurrentPopup();
    }
}
