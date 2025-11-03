using CkCommons;
using CkCommons.Chat;
using CkCommons.Gui;
using CkCommons.Helpers;
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
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.Services.Tutorial;
using Sundouleia.WebAPI;
using SundouleiaAPI.Network;
using System.Globalization;

namespace Sundouleia.Radar.Chat;
public class RadarChatLog : CkChatlog<RadarCkChatMessage>, IMediatorSubscriber, IDisposable
{
    private static RichTextFilter AllowedTypes = RichTextFilter.Emotes;

    private readonly ILogger<RadarChatLog> _logger;
    private readonly MainHub _hub;
    private readonly MainMenuTabs _tabMenu;
    private readonly SundesmoManager _sundesmos;
    private readonly RadarService _service;
    private readonly TutorialService _guides;

    // Private variables that are used by internal methods.
    private bool _emoteSelectionOpened = false;
    private unsafe bool _isInside => HousingManager.Instance()->IsInside();

    // Allow a circular buffer of 1k messages.
    public RadarChatLog(ILogger<RadarChatLog> logger, SundouleiaMediator mediator, MainHub hub,
        MainMenuTabs tabs, SundesmoManager sundesmos, RadarService service, TutorialService guides) 
        : base(0, "Radar Chat", 1000)
    {
        _logger = logger;
        Mediator = mediator;
        _hub = hub;
        _tabMenu = tabs;
        _sundesmos = sundesmos;
        _service = service;
        _guides = guides;

        Mediator.Subscribe<NewRadarChatMessage>(this, msg => AddNetworkMessage(msg.Message, msg.FromSelf));
        Mediator.Subscribe<MainWindowTabChangeMessage>(this, msg =>
        {
            if (msg.NewTab is MainMenuTabs.SelectedTab.RadarChat)
            {
                ShouldScrollToBottom = true;
                unreadSinceScroll = 0;
                NewMsgCount = 0;
                NewCorbyMsg = false;
            }
        });
        Mediator.Subscribe<RadarTerritoryChanged>(this, msg => ChangeChatLog(msg.PrevTerritory, msg.NewTerritory));

        // Should just end up null or something empty.
        LoadTerritoryChatLog(RadarService.CurrWorld, RadarService.CurrZone);
    }

    public SundouleiaMediator Mediator { get; }

    // Config options cant stop the corby >:3
    public static bool AccessBlocked => ChatBlocked || NotVerified;
    public static bool ChatBlocked => !MainHub.Reputation.ChatUsage;
    public static bool NotVerified => !MainHub.Reputation.IsVerified;
    public static bool NewCorbyMsg { get; private set; } = false;
    public static int NewMsgCount { get; private set; } = 0;

    void IDisposable.Dispose()
    {
        Mediator.UnsubscribeAll(this);
        // Save the chat log prior to disposal.
        SaveChatLog(RadarService.CurrWorld, RadarService.CurrZone);
        GC.SuppressFinalize(this);
    }

    private static string GetRecentFile(ushort worldId, ushort zoneId)
    {
        var cfgName = $"{worldId}-{zoneId}-recent-chat.log";
        return Path.Combine(ConfigFileProvider.ChatDirectory, cfgName);
    }

    public void SetDisabledStates(bool content, bool input)
    {
        disableContent = content;
        disableInput = input;
    }

    public void SetAutoScroll (bool newState)
        => DoAutoScroll = newState;

    protected override string ToTooltip(RadarCkChatMessage message)
        => $"Sent @ {message.Timestamp.ToString("T", CultureInfo.CurrentCulture)}" +
        "--NL----COL--[Middle-Click]--COL-- Open Profile";

    private void AddNetworkMessage(RadarChatMessage message, bool fromSelf)
    {
        // 1) Filter out blocked users here.


        // 2) Update the chat contents.
        if (_tabMenu.TabSelection is not MainMenuTabs.SelectedTab.RadarChat)
            NewMsgCount++;
        else
        {
            NewMsgCount = 0;
            NewCorbyMsg = false;
        }

        // 3) Initial assumption of the sender name.
        var finalName = message.Sender.AnonName; // "Anon.User-XXXX"
        // 4) Adjust sender name based on special conditions.
        if (message.Sender.Tier is CkVanityTier.ShopKeeper)
            finalName = $"Cordy";
        else if (_sundesmos.GetUserOrDefault(message.Sender) is { } sundesmo)
            finalName = $"{sundesmo.GetNickAliasOrUid()} ({message.Sender.UID[..4]})";
        else if (fromSelf)
            finalName = $"{message.Sender.AliasOrUID} ({message.Sender.UID[..4]})";
        
        // Construct the network message to the RadarCkChatMessage format.
        AddMessage(new RadarCkChatMessage(message.Sender, finalName, message.Message));
    }

    protected override void AddMessage(RadarCkChatMessage newMsg)
    {
        // Cordy is special girl :3
        if (newMsg.Tier is CkVanityTier.ShopKeeper)
        {
            // Apply Cordy's signature color to her name, and also prefix her icon and give all RichText permissions.
            UserColors[newMsg.UID] = CkColor.ShopKeeperColor.Vec4();
            var prefix = $"[img=RequiredImages\\Tier4Icon][rawcolor={CkColor.ShopKeeperColor.Uint()}]{newMsg.Name}[/rawcolor]: ";
            Messages.PushBack(newMsg with { Message = $"{prefix} [rawcolor={CkColor.ShopKeeperText.Uint()}]{newMsg.Message}[/rawcolor]" });
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
        if (shouldFocusChatInput && ImGui.IsWindowFocused())
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
        using (ImRaii.PushColor(ImGuiCol.Text, CkColor.VibrantPink.Uint(), _emoteSelectionOpened))
        {
            if (CkGui.IconButton(FAI.Heart, disabled: disableInput))
                _emoteSelectionOpened = !_emoteSelectionOpened;
        }
        CkGui.AttachToolTip($"Toggles Quick-Emote selection.", disableInput);
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ChatEmotes, ImGui.GetWindowPos(), ImGui.GetWindowSize());

        // Toggle AutoScroll functionality
        ImUtf8.SameLineInner();
        if (CkGui.IconButton(scrollIcon, disabled: disableInput))
            DoAutoScroll = !DoAutoScroll;
        CkGui.AttachToolTip($"Toggles AutoScroll (Current: {(DoAutoScroll ? "Enabled" : "Disabled")})");
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ChatScroll, ImGui.GetWindowPos(), ImGui.GetWindowSize());

        // draw the popout button
        ImUtf8.SameLineInner();
        if (CkGui.IconButton(FAI.Expand, disabled: disableInput || !KeyMonitor.ShiftPressed()))
            Mediator.Publish(new UiToggleMessage(typeof(RadarChatPopoutUI)));
        CkGui.AttachToolTip("Open a Popout of the Radar Chat!--SEP--Hold SHIFT to activate!");
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

    protected override void OnMiddleClick(RadarCkChatMessage message)
    {
        if (disableContent) return;
        Mediator.Publish(new ProfileOpenMessage(message.UserData));
    }

    protected override void OnSendMessage(string message)
    {
        shouldFocusChatInput = true;
        if (string.IsNullOrWhiteSpace(previewMessage))
            return;
        // Send message to the server
        _hub.RadarChatMessage(new(MainHub.OwnUserData, RadarService.CurrWorld, RadarService.CurrZone, previewMessage)).ConfigureAwait(false);
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
                // TODO:
                UiService.SetUITask(async () => await _hub.UserBlock(new(LastInteractedMsg.UserData)));
                ClosePopupAndResetMsg();
            }
        CkGui.AttachToolTip(shiftHeld ? $"Blocks {LastInteractedMsg.Name} permanently." : "Must be holding CTRL+SHIFT to select.");
    }

    private void ClosePopupAndResetMsg()
    {
        LastInteractedMsg = null;
        ImGui.CloseCurrentPopup();
    }

    private void SaveChatLog(ushort worldId, ushort zoneId)
    {
        if (worldId == 0 || zoneId == 0 || _isInside)
            return;

        // Capture up to the last 500 messages
        var messagesToSave = Messages.TakeLast(500).ToList();
        var logToSave = new SerializableChatLog(worldId, zoneId, TimeCreated, messagesToSave);

        // Serialize the item to JSON
        try
        {
            var json = JsonConvert.SerializeObject(logToSave);
            var compressed = json.Compress(6);
            var base64ChatLogData = Convert.ToBase64String(compressed);
            File.WriteAllText(GetRecentFile(worldId, zoneId), base64ChatLogData);
        }
        catch (DirectoryNotFoundException) { /* Swallow */ }
        catch (FileNotFoundException) { /* Swallow */ }
        catch (Bagagwa ex)
        {
            _logger.LogError($"Failed to compress chat log: {ex}");
        }
    }

    private void ChangeChatLog(ushort prevLoc, ushort newLoc)
    {
        // Save the current chat log first.
        SaveChatLog(RadarService.CurrWorld, prevLoc);
        // Clear the current chat log.
        Messages.Clear();
        UserColors.Clear();
        unreadSinceScroll = 0;
        NewMsgCount = 0;
        NewCorbyMsg = false;
        // Load the new territory chat log.
        LoadTerritoryChatLog(RadarService.CurrWorld, newLoc);
    }

    public void LoadTerritoryChatLog(ushort worldId, ushort zoneId)
    {
        if (worldId == 0 || zoneId == 0 || _isInside)
            return;

        var recentFile = GetRecentFile(worldId, zoneId);
        // if the file does not exist, return
        if (!File.Exists(recentFile))
        {
            AddDefaultWelcomeWithLog("Chat log file does not exist.");
            return;
        }

        // Maybe it exists, but we are not in the same territory. If that is the case we should add the welcome message and return.
        // TODO: handle this.
        // Attempt Deserialization.
        var savedChatlog = new SerializableChatLog();
        try
        {
            var base64logFile = File.ReadAllText(recentFile);
            var bytes = Convert.FromBase64String(base64logFile);
            var version = bytes[0];
            version = bytes.DecompressToString(out var decompressed);
            savedChatlog = JsonConvert.DeserializeObject<SerializableChatLog>(decompressed);

            // if any user datas are null, throw an exception.
            if (savedChatlog.Messages.Any(m => m.UserData is null))
                throw new Exception("One or more user datas are null in the chat log.");
        }
        catch (DirectoryNotFoundException) { /* Swallow */ }
        catch (FileNotFoundException) { /* Swallow */ }
        catch (Bagagwa ex)
        {
            AddDefaultWelcomeWithLog("Failed to decompress chat log: " + ex);
            return;
        }

        // Do not restore if on a different day than the recovered log.
        if (savedChatlog.DateStarted.DayOfYear != DateTime.Now.DayOfYear)
        {
            AddDefaultWelcomeWithLog("Chat log is from a different day. Not restoring.");
            return;
        }

        // print out all messages:
        foreach (var msg in savedChatlog.Messages)
        {
            // Cordy is special girl :3
            if (msg.Tier is CkVanityTier.ShopKeeper)
                UserColors[msg.UID] = CkColor.ShopKeeperColor.Vec4();
            else
                AssignSenderColor(msg);

            Messages.PushBack(msg);
            unreadSinceScroll++;
        }

        _logger.LogInformation($"Loaded {savedChatlog.Messages.Count} messages from the chat log.", LoggerType.RadarChat);

        // On Failed Loads
        void AddDefaultWelcomeWithLog(string logMessage)
        {
            // Maybe replace with territory intended use later or something.
            _logger.LogWarning(logMessage);
            AddMessage(new(new("System"), "System",
                $"[color=grey2]Welcome to {RadarService.CurrZoneName}'s Radar Chat! Your Name displays as " +
                $"[color=yellow]{MainHub.OwnUserData.AnonName}[/color] to others! Feel free to say hi![/color][line]"));
        }
    }
}
