using CkCommons;
using CkCommons.Chat;
using CkCommons.Gui;
using CkCommons.Helpers;
using CkCommons.Raii;
using CkCommons.RichText;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.WebAPI;
using SundouleiaAPI.Network;
using System.Globalization;

namespace Sundouleia.Utils;
public class PopoutRadarChatlog : CkChatlog<RadarCkChatMessage>, IMediatorSubscriber, IDisposable
{
    private static string RecentFile => Path.Combine(ConfigFileProvider.SundouleiaDirectory, "recent-chat.log");
    private static RichTextFilter AllowedTypes = RichTextFilter.Emotes;

    private readonly MainHub _hub;
    private readonly SundesmoManager _sundesmos;

    private bool _showEmotes = false;
    public PopoutRadarChatlog(SundouleiaMediator mediator, MainHub hub, SundesmoManager pairs) 
        : base(1, "Popout Radar Chat", 1000)
    {
        Mediator = mediator;
        _hub = hub;
        _sundesmos = pairs;

        // Load the chat log from most recent session, if any.
        LoadTerritoryChatLog();

        Mediator.Subscribe<NewRadarChatMessage>(this, msg => AddNetworkMessage(msg.Message, msg.FromSelf));
        Mediator.Subscribe<MainWindowTabChangeMessage>(this, (msg) =>
        {
            if (msg.NewTab is MainMenuTabs.SelectedTab.RadarChat)
                ShouldScrollToBottom = true;
        });
    }

    public SundouleiaMediator Mediator { get; }

    void IDisposable.Dispose()
    {
        Mediator.UnsubscribeAll(this);
        GC.SuppressFinalize(this);
    }

    public void SetAutoScroll (bool newState)
        => DoAutoScroll = newState;

    protected override string ToTooltip(RadarCkChatMessage message)
        => $"Sent @ {message.Timestamp.ToString("T", CultureInfo.CurrentCulture)}" +
        "--NL----COL--[Right-Click]--COL-- View Interactions" +
        "--NL----COL--[Middle-Click]--COL-- Open Profile";

    private void AddNetworkMessage(RadarChatMessage message, bool fromSelf)
    {
        // 1) Add later here a filter for our blocker list.


        // 2) Initial assumption of the sender name.
        var finalName = message.Sender.AnonName; // "Anon.User-XXXX"
        // 3) Adjust sender name based on special conditions.
        if (message.Sender.Tier is CkVanityTier.ShopKeeper)
            finalName = $"Cordy";
        else if (_sundesmos.GetUserOrDefault(message.Sender) is { } sundesmo)
            finalName = $"{sundesmo.GetNickAliasOrUid()} ({message.Sender.UID[..4]})";
        else if (fromSelf)
            finalName = $"{message.Sender.AliasOrUID} ({message.Sender.UID[..4]})";

        // 4) Construct the network message to the RadarCkChatMessage format.
        AddMessage(new RadarCkChatMessage(message.Sender, finalName, message.Message));
    }

    protected override void AddMessage(RadarCkChatMessage newMsg)
    {
        // Cordy is special girl :3
        if (newMsg.Tier is CkVanityTier.ShopKeeper)
        {
            // Force set the uid color to her favorite color.
            UserColors[newMsg.UID] = CkColor.CkMistressColor.Vec4();
            // allow any rich text tags, as she is a special case.
            var prefix = $"[img=RequiredImages\\Tier4Icon][rawcolor={CkColor.CkMistressColor.Uint()}]{newMsg.Name}[/rawcolor]: ";
            Messages.PushBack(newMsg with { Message = prefix + newMsg.Message });
            unreadSinceScroll++;
        }
        else if (newMsg.UID == "System")
        {
            // System messages are special, they are not colored.
            var prefix = $"[rawcolor=0xFF0000FF]{newMsg.Name}[/rawcolor]: ";
            Messages.PushBack(newMsg with { Message = prefix + newMsg.Message });
            unreadSinceScroll++;
        }
        else
        {
            // Assign the sender color
            var col = AssignSenderColor(newMsg).ToUint();
            // strip out the modifiers that are not allowed to prevent chaos in global chat.
            var sanitizedMsg = CkRichText.StripDisallowedRichTags(newMsg.Message, AllowedTypes);
            // append special formatting to the start of the message based on supporter type.
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

    private void AddExistingMessage(RadarCkChatMessage newMsg)
    {
        // Cordy is special girl :3
        if (newMsg.Tier is CkVanityTier.ShopKeeper)
        {
            // Force set the uid color to her favorite color.
            UserColors[newMsg.UID] = CkColor.CkMistressColor.Vec4();
            Messages.PushBack(newMsg);
            unreadSinceScroll++;
        }
        else
        {
            // Assign the sender color
            AssignSenderColor(newMsg);
            Messages.PushBack(newMsg);
            unreadSinceScroll++;
        }
    }

    public override void DrawChatInputRow()
    {
        using var _ = ImRaii.Group();

        var scrollIcon = DoAutoScroll ? FAI.ArrowDownUpLock : FAI.ArrowDownUpAcrossLine;
        var width = ImGui.GetContentRegionAvail().X;

        // Set keyboard focus to the chat input box if needed
        if (shouldFocusChatInput && ImGui.IsWindowFocused())
        {
            ImGui.SetKeyboardFocusHere(0);
            shouldFocusChatInput = false;
        }

        ImGui.SetNextItemWidth(width - (CkGui.IconButtonSize(scrollIcon).X + ImGui.GetStyle().ItemInnerSpacing.X) * 3);
        ImGui.InputTextWithHint($"##ChatInput{Label}{ID}", "type here...", ref previewMessage, 300);

        // Process submission Prevent losing chat focus after pressing the Enter key.
        if (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            shouldFocusChatInput = true;
            OnSendMessage(previewMessage);
        }

        // toggle emote viewing.
        ImUtf8.SameLineInner();
        if (CkGui.IconButton(FAI.Heart))
            _showEmotes = !_showEmotes;
        CkGui.AttachToolTip($"Toggles Quick-Emote selection.");

        // Toggle AutoScroll functionality
        ImUtf8.SameLineInner();
        if (CkGui.IconButton(scrollIcon))
            DoAutoScroll = !DoAutoScroll;
        CkGui.AttachToolTip($"Toggles AutoScroll (Current: {(DoAutoScroll ? "Enabled" : "Disabled")})");

        // draw the popout button
        ImUtf8.SameLineInner();
        if (CkGui.IconButton(FAI.Expand, disabled: !KeyMonitor.ShiftPressed()))
            Mediator.Publish(new UiToggleMessage(typeof(RadarChatPopoutUI)));
        CkGui.AttachToolTip("Open the Global Chat in a Popout Window--SEP--Hold SHIFT to activate!");
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
        if (_showEmotes)
        {
            var drawPos = drawTextPreview ? ImGui.GetItemRectMin() : inputPosMin;
            DrawQuickEmoteWindow(drawPos);
        }
    }

    private void DrawQuickEmoteWindow(Vector2 drawPos)
    {
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var emoteCache = CosmeticService.EmoteTextures.Cache;
        var totalEmotes = emoteCache.Count;
        var emoteSize = new Vector2(ImGui.GetFrameHeightWithSpacing());
        var emotesPerRow = Math.Max(1, (int)((totalWidth.RemoveWinPadX() + spacing) / (emoteSize.X + spacing)));
        var rows = (int)Math.Ceiling((float)totalEmotes / emotesPerRow);
        var winHeight = (emoteSize.Y + spacing) * rows - spacing;

        // Draw the emote window at the bottom of the chat input.
        var winPos = drawPos - new Vector2(0, winHeight.AddWinPadY());
        ImGui.SetNextWindowPos(winPos);
        using var c = CkRaii.ChildPaddedW("Quick-Emote-View", totalWidth, winHeight, wFlags: WFlags.AlwaysVerticalScrollbar);

        var wdl = ImGui.GetWindowDrawList();
        wdl.PushClipRect(winPos, winPos + c.InnerRegion.WithWinPadding(), false);
        wdl.AddRectFilled(winPos, winPos + c.InnerRegion.WithWinPadding(), 0xCC000000, 5, ImDrawFlags.RoundCornersAll);
        wdl.AddRect(winPos, winPos + c.InnerRegion.WithWinPadding(), CkColor.LushPinkLine.Uint(), 5, ImDrawFlags.RoundCornersAll);
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

    protected override void OnMiddleClick(RadarCkChatMessage message)
        => Mediator.Publish(new ProfileOpenMessage(message.UserData));

    protected override void OnSendMessage(string message)
    {
        shouldFocusChatInput = true;
        if (string.IsNullOrWhiteSpace(previewMessage))
            return;
        // Send message to the server
        _hub.RadarChatMessage(new(MainHub.OwnUserData, RadarChatLog.WorldLoc, RadarChatLog.TerritoryLoc, previewMessage)).ConfigureAwait(false);
        // Clear preview
        previewMessage = string.Empty;
    }

    protected override void DrawPopupInternal()
    {
        if (LastInteractedMsg is null)
            return;

        var shiftHeld = KeyMonitor.ShiftPressed();
        var ctrlHeld = KeyMonitor.CtrlPressed();
        var isSystemMsg = LastInteractedMsg.UID == "System";
        var isOwnMsg = LastInteractedMsg.UID == MainHub.UID;
        var disableSilence = !ctrlHeld || isSystemMsg || isOwnMsg;
        var disableBlock = !shiftHeld || !disableSilence;

        // Profile Viewing
        CkGui.FontText(LastInteractedMsg.Name, Svc.PluginInterface.UiBuilder.MonoFontHandle, ImGuiColors.ParsedGold);
        ImGui.Separator();

        // Temp Silencing.
        using (ImRaii.Disabled(disableSilence))
            if (ImGui.Selectable("Hide Messages from User") && !isOwnMsg && !isSystemMsg)
            {
                SilenceList.Add(LastInteractedMsg.UID);
                ClosePopupAndResetMsg();
            }
        CkGui.AttachToolTip(ctrlHeld ? $"Hides messages from {LastInteractedMsg.Name} until plugin reload/restart." : "Must be holding CTRL to select.");

        // Permanent Blocking.
        using (ImRaii.Disabled(disableBlock))
            if (ImGui.Selectable("Block User") && !isOwnMsg && !isSystemMsg)
            {
                // probably do extra handling here for updating the blocker list after.
                UiService.SetUITask(async () => await _hub.Callback_Blocked(new(LastInteractedMsg.UserData)));
                ClosePopupAndResetMsg();
            }
        CkGui.AttachToolTip(shiftHeld ? $"Blocks {LastInteractedMsg.Name} permanently." : "Must be holding CTRL+SHIFT to select.");
    }

    private void ClosePopupAndResetMsg()
    {
        LastInteractedMsg = null;
        ImGui.CloseCurrentPopup();
    }

    public void LoadTerritoryChatLog()
    {
        // if the file does not exist, return
        if (!File.Exists(RecentFile))
        {
            AddDefaultWelcome();
            return;
        }

        // Maybe it exists, but we are not in the same territory. If that is the case we should add the welcome message and return.
        // TODO: handle this.

        // Attempt Deserialization.
        var savedChatlog = new SerializableChatLog();
        try
        {
            var base64logFile = File.ReadAllText(RecentFile);
            var bytes = Convert.FromBase64String(base64logFile);
            var version = bytes[0];
            version = bytes.DecompressToString(out var decompressed);
            savedChatlog = JsonConvert.DeserializeObject<SerializableChatLog>(decompressed);

            // if any user datas are null, throw an exception.
            if (savedChatlog.Messages.Any(m => m.UserData is null))
                throw new Exception("One or more user datas are null in the chat log.");
        }
        catch (Bagagwa)
        {
            AddDefaultWelcome();
            return;
        }

        // Do not restore if on a different day than the recovered log.
        if (savedChatlog.DateStarted.DayOfYear != DateTime.Now.DayOfYear)
        {
            AddDefaultWelcome();
            return;
        }

        // print out all messages:
        foreach (var msg in savedChatlog.Messages)
        {
            // Cordy is special girl :3
            if (msg.Tier is CkVanityTier.ShopKeeper)
                UserColors[msg.UID] = CkColor.CkMistressColor.Vec4();
            else
                AssignSenderColor(msg);

            Messages.PushBack(msg);
            unreadSinceScroll++;
        }

        // On Failed Loads
        void AddDefaultWelcome()
        {
            AddMessage(new(new("System"), "System", "Welcome to the Radar Chat![para]" +
                "Your Name in here is Anonymous to anyone you have not yet added. Feel free to say hi![line]"));
        }
    }
}
