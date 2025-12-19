using CkCommons;
using CkCommons.Classes;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Text;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui;

public class ProfilesTab
{
    private const string CHARA_DRAGDROP_LABEL = "CHARA_AUTH_MOVE";

    private readonly ILogger<ProfilesTab> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly MainConfig _mainConfig;
    private readonly AccountManager _account;

    public ProfilesTab(ILogger<ProfilesTab> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, AccountManager account)
    {
        _logger = logger;
        _mediator = mediator;
        _hub = hub;
        _mainConfig = config;
        _account = account;
    }

    private int _selectedProfileIdx = -1;
    private HashSet<CharaAuthentication> _assignedCharas = new();
    private HashSet<CharaAuthentication> _otherCharas = new();

    private (bool WasAssigned, CharaAuthentication Chara)? _draggedChara;
    private Action? _dragDropAction;

    private bool _draggingAssigned => _draggedChara != null && _draggedChara.Value.WasAssigned;
    private bool _draggingUnassigned => _draggedChara != null && !_draggedChara.Value.WasAssigned;

    // Fix later.
    private float LeftWidth => ImGuiHelpers.GlobalScale * 250f;

    // Right now this is going to be a mess since the authentications are structured differently.
    // for now we will instead just display basic information and let the rest happen after we see it working.
    public void DrawManager()
    {
        using var _ = CkRaii.Child("ProfilesOuterChild", ImGui.GetContentRegionAvail());

        DrawProfileList(_.InnerRegion.Y);

        ImUtf8.SameLineInner();
        using (ImRaii.Group())
        {
            var profile = _account.Config.Profiles.GetValueOrDefault(_selectedProfileIdx);
            DrawSelectedProfile(profile);
            DrawUnassignedProfileCharacters(profile);
        }

        ProcessDragDrop();
    }

    private void ProcessDragDrop()
    {
        var draggingItemPresent = _draggedChara is not null;
        // If we are dragging something, and we release the mouse button, clear the dragged item.
        if (draggingItemPresent && _dragDropAction is not null)
        {
            _dragDropAction.Invoke();
            _dragDropAction = null;
        }

        if (draggingItemPresent && ImGuiUtil.IsDropping(CHARA_DRAGDROP_LABEL))
        {
            _draggedChara = null;
        }
    }

    private void DrawProfileList(float height)
    {
        // ImGui throws asserts with child objects in child objects off screen if not protected by a third child object.
        // Idk why the hell this happens but it does, and this is how to prevent it.
        using var outer = CkRaii.FramedChildPaddedH("ProfileList", LeftWidth, height, 0, CkColor.VibrantPink.Uint(), CkStyle.ChildRounding());

        var pos = ImGui.GetCursorScreenPos();
        CkGui.FontText("Profiles", UiFontService.UidFont);
        CkGui.Separator(CkColor.VibrantPink.Uint());
        ImGui.Spacing();

        using (var _ = CkRaii.Child("ProfileListInner", ImGui.GetContentRegionAvail()))
        {
            // Format Profiles.
            using var s = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 5f)
                .Push(ImGuiStyleVar.WindowBorderSize, 1f);
            using var c = ImRaii.PushColor(ImGuiCol.Border, CkColor.VibrantPink.Uint())
                .Push(ImGuiCol.ChildBg, new Vector4(0.25f, 0.2f, 0.2f, 0.4f));

            var bgCol = new Vector4(0.25f, 0.2f, 0.2f, 0.4f).ToUint();
            var rounding = CkStyle.ListItemRounding();
            var frame = 1f;
            var size = new Vector2(_.InnerRegion.X, ImUtf8.TextHeight.AddWinPadY());
            foreach (var (idx, profile) in _account.Config.Profiles)
                DrawProfileBox(idx, profile, size, _selectedProfileIdx == idx, bgCol, rounding, frame);
        }

        ImGui.SetCursorScreenPos(pos + new Vector2(outer.InnerRegion.X - ImUtf8.FrameHeight, 0));
        CkGui.FramedHoverIconText(FAI.QuestionCircle, ImGuiColors.TankBlue.ToUint(), ImGui.GetColorU32(ImGuiCol.TextDisabled));
        CkGui.AttachToolTip("The Profiles associated with your Sundouleia Account." +
            "--NL--Deleting the primary profile --COL--removes all other profiles.--COL--", ImGuiColors.DalamudRed);
    }

    private void DrawProfileBox(int idx, AccountProfile profile, Vector2 size, bool selected, uint bgCol, float rounding, float frameWidth)
    {
        var label = $"{profile.UserUID}-{profile.ProfileLabel}";
        var frameCol = profile.IsPrimary ? CkColor.VibrantPinkHovered.Uint() : ImGuiColors.ParsedGold.ToUint();

        using (CkRaii.FramedChildPaddedWH($"{profile.UserUID}-{profile.ProfileLabel}", size, bgCol, selected ? frameCol : 0, rounding, wFlags: WFlags.ChildWindow))
        {
            using (ImRaii.Group())
            {
                CkGui.IconText(FAI.IdBadge);
                CkGui.TextInline(profile.UserUID);
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - CkGui.IconSize(FAI.PlugCircleCheck).X);
                CkGui.IconText(profile.HadValidConnection ? FAI.PlugCircleCheck : FAI.PlugCircleXmark, profile.HadValidConnection ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
                CkGui.AttachToolTip(profile.HadValidConnection ? "A Valid Connection was established.--SEP--" : "No Valid connection created with this account.--SEP--");
            }
            CkGui.AttachToolTip("Double-Click to select this profile.--NL--Right-Click to deselect.");
        }
        if (ImGui.IsItemHovered())
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _selectedProfileIdx = idx;
                RecreateAuthLists();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _selectedProfileIdx = -1;
                RecreateAuthLists();
            }
        }
    }

    // Maybe move this into above variable.
    private void DrawSelectedProfile(AccountProfile? profile)
    {
        var region = ImGui.GetContentRegionAvail();
        using var _ = CkRaii.FramedChildPaddedW("SelectedProfile", region.X, region.Y * .65f, 0, CkColor.VibrantPink.Uint(), CkStyle.ChildRounding());

        if (profile is null)
        {
            CkGui.FontText("No Profile Selected", UiFontService.UidFont);
            CkGui.Separator(CkColor.VibrantPink.Uint());
            return;
        }

        CkGui.FontText(profile.UserUID, UiFontService.UidFont);
        CkGui.AttachToolTip("This Profile's UID");
        // Cak put a key icon to the right or whatever but future Corby issue™
        CkGui.Separator(CkColor.VibrantPink.Uint());
        ImGui.Spacing();

        var green = CkColor.TriStateCheck.Vec4();
        var bgCol = _draggingUnassigned ? CkGui.Color(Gradient.Get(green, green with { W = green.W / 4 }, 500)) : 0;
        using (var __ = CkRaii.Child("AssignedCharas", ImGui.GetContentRegionAvail(), bgCol))
        {
            var entryCol = new Vector4(0.25f, 0.2f, 0.2f, 0.4f).ToUint();
            foreach (var chara in _assignedCharas.ToList())
                DrawAssignedCharacter(profile, chara, __.InnerRegion.X, entryCol);
        }

        if (_draggingUnassigned && ImGui.IsWindowHovered())
        {
            using var target = ImRaii.DragDropTarget();
            if (target.Success && ImGuiUtil.IsDropping(CHARA_DRAGDROP_LABEL))
                _dragDropAction = AssignCharaToProfile;
        }
    }

    private void DrawUnassignedProfileCharacters(AccountProfile? profile)
    {
        var region = ImGui.GetContentRegionAvail();
        using var _ = CkRaii.FramedChildPaddedWH("UnassignedCharas", region, 0, CkColor.VibrantPink.Uint(), CkStyle.ChildRounding());

        // Display the attached profiles below in a child element.
        var green = CkColor.TriStateCheck.Vec4();
        var bgCol = _draggingAssigned ? CkGui.Color(Gradient.Get(green, green with { W = green.W / 4 }, 500)) : 0;

        using (var __ = CkRaii.Child("UnassignedCharas", _.InnerRegion, bgCol))
        {
            if (profile is null)
                return;

            var itemSize = new Vector2(__.InnerRegion.X, ImUtf8.FrameHeight);
            var entryCol = new Vector4(0.25f, 0.2f, 0.2f, 0.4f).ToUint();
            foreach (var chara in _otherCharas.ToList())
                DrawUnassignedCharacter(profile, chara, __.InnerRegion.X, entryCol);
        }

        if (_draggingAssigned)
        {
            using var target = ImRaii.DragDropTarget();
            if (target.Success && ImGuiUtil.IsDropping(CHARA_DRAGDROP_LABEL))
                _dragDropAction = RemoveCharaFromProfile;
        }
    }

    private void DrawAssignedCharacter(AccountProfile profile, CharaAuthentication chara, float width, uint bgCol)
    {
        using (CkRaii.Group(bgCol, CkStyle.ListItemRounding(), CkStyle.ThinThickness()))
        {
            var pos = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"assigned-inv-{chara.PlayerName}-{chara.ContentId}", new Vector2(width, ImUtf8.FrameHeight));
            ImGui.SetCursorPos(pos);

            CkGui.FramedIconText(FAI.UserCircle);
            CkGui.TextFrameAlignedInline(chara.PlayerName);
            ImGui.SameLine();
            CkGui.RightFrameAligned(GameDataSvc.WorldData.TryGetValue(chara.WorldId, out var name) ? name : "UNK_WORLD", 10f);
            CkGui.AttachToolTip("You can drag this character to the below box to remove it from this profile!");
        }
        CkGui.AttachToolTip($"This Character uses Profile {profile.UserUID}.");

        using var source = ImRaii.DragDropSource();
        if (!source) return;
        // Ignore data as we store it internally.
        ImGui.SetDragDropPayload(CHARA_DRAGDROP_LABEL, null, 0);
        _draggedChara = (true, chara);
        CkGui.ColorText($"Removing {chara.PlayerName} from {profile.UserUID}..", ImGuiColors.DalamudYellow);
    }

    private void DrawUnassignedCharacter(AccountProfile profile, CharaAuthentication chara, float width, uint bgCol)
    {
        using (CkRaii.FramedChildPaddedW($"unassigned-{chara.PlayerName}-{chara.ContentId}", width, ImUtf8.FrameHeight, bgCol, 0, FancyTabBar.RoundingInner))
        {
            using (ImRaii.Group())
            {
                CkGui.IconText(FAI.UserCircle);
                CkGui.TextInline(chara.PlayerName);
                if (chara.ProfileIdx != -1 && _account.Config.Profiles.TryGetValue(chara.ProfileIdx, out var prof))
                {
                    ImGui.SameLine();
                    CkGui.TagLabelText(prof.UserUID, ImGuiColors.DalamudGrey3, 3 * ImGuiHelpers.GlobalScale);
                }
                ImGui.SameLine();
                CkGui.RightAligned(GameDataSvc.WorldData.TryGetValue(chara.WorldId, out var name) ? name : "UNK_WORLD", 10f);
            }
            CkGui.AttachToolTip("You can drag this character to the above box to attach this character to the selected profile!");
        }
        CkGui.AttachToolTip($"This Character does not use Profile {profile.UserUID}.");


        using var source = ImRaii.DragDropSource();
        if (!source) return;
        // Ignore data as we store it internally.
        ImGui.SetDragDropPayload(CHARA_DRAGDROP_LABEL, null, 0);
        _draggedChara = (false, chara);
        CkGui.ColorText($"Adding {chara.PlayerName} to {profile.UserUID}..", ImGuiColors.DalamudYellow);
    }

    private void AssignCharaToProfile()
    {
        if (_draggedChara is not { } item)
            return;

        if (_selectedProfileIdx == -1)
            return;

        // Store the previous idx.
        var prevIdx = item.Chara.ProfileIdx;
        // Update it.
        item.Chara.ProfileIdx = _selectedProfileIdx;
        _account.SaveConfig();

        // Check the account of the profile we moved from, and the one we moved to.
        // If either was the currently logged in account, perform a reconnect.
        if (_account.Config.Profiles.TryGetValue(prevIdx, out var previous) && previous.UserUID == MainHub.UID)
            _ = _hub.Reconnect();  // Will need to fix this, as reconnects are kind of busted atm.
        else if (prevIdx == -1 && _selectedProfileIdx != -1)
        {
            // Grab the current player auth.
            if (_account.TryGetAuthForPlayer(out var auth) && auth.ContentId == PlayerData.ContentIdInstanced)
                _ = _hub.Reconnect();  // Will need to fix this, as reconnects are kind of busted atm.
        }

        // Clear the dragged chara.
        _draggedChara = null;
        // Recreate lists.
        RecreateAuthLists();
    }

    private void RemoveCharaFromProfile()
    {
        if (_draggedChara is not { } item)
            return;

        if (item.Chara.ProfileIdx == -1)
            return;

        _logger.LogInformation($"I would have removed your chara from this profile! {item.Chara.PlayerName ?? "UNK"}");
        return;

        //var prevIdx = item.Chara.ProfileIdx;
        //item.Chara.ProfileIdx = -1;
        //_account.SaveConfig();

        //// Logout if the previous IDX maps to our current character.
        //if (_account.Config.Profiles.TryGetValue(prevIdx, out var profile) && profile.UserUID == MainHub.UID)
        //    _ = _hub.Reconnect(); // Will need to fix this, as reconnects are kind of busted atm.
    }

    private void RecreateAuthLists()
    {
        _assignedCharas.Clear();
        _otherCharas.Clear();
        foreach (var auth in _account.Config.LoginAuths.ToList())
        {
            if (auth.ProfileIdx == _selectedProfileIdx)
                _assignedCharas.Add(auth);
            else
                _otherCharas.Add(auth);
        }
    }

    //if (ImGui.BeginPopupModal("Delete your account?", ref DeleteAccountConfirmation, WFlags.NoResize | WFlags.NoScrollbar | WFlags.NoScrollWithMouse))
    //{
    //    if (isPrimary)
    //    {
    //        CkGui.ColorTextWrapped(CkLoc.Settings.Accounts.RemoveAccountPrimaryWarning, ImGuiColors.DalamudRed);
    //        ImGui.Spacing();
    //    }
    //    // display normal warning
    //    CkGui.TextWrapped(CkLoc.Settings.Accounts.RemoveAccountWarning);
    //    ImGui.TextUnformatted(CkLoc.Settings.Accounts.RemoveAccountConfirm);
    //    ImGui.Separator();
    //    ImGui.Spacing();

    //    var buttonSize = (ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X - ImGui.GetStyle().ItemSpacing.X) / 2;

    //    if (CkGui.IconTextButton(FAI.Trash, CkLoc.Settings.Accounts.DeleteButtonLabel, buttonSize, false, !(KeyMonitor.CtrlPressed() && KeyMonitor.ShiftPressed())))
    //    {
    //        _ = RemoveAccountAndRelog(account, isPrimary);
    //    }
    //    CkGui.AttachToolTip("CTRL+SHIFT Required");

    //    ImGui.SameLine();

    //    if (ImGui.Button("Cancel##cancelDelete", new Vector2(buttonSize, 0)))
    //        DeleteAccountConfirmation = false;

    //    CkGui.SetScaledWindowSize(325);
    //    ImGui.EndPopup();
    //}

    // Revise this!!!! It behaves differently in sundouleia!
    private async Task RemoveAccountProfile(CharaAuthentication chara, AccountProfile profile)
    {
        await Task.Delay(1).ConfigureAwait(false);

        // grab the uid before we delete the user.
        var uid = MainHub.UID;
        //    _logger.LogInformation("Removing Authentication for current character.");
        //    _account.Config.Profiles.Remove();
        //    // In theory this should have automatic cleanup once the new account management stuff works out.
        //    if (isPrimary)
        //    {
        //        _serverConfigs.ServerStorage.Authentications.Clear();
        //        _mainConfig.Current.AcknowledgementUnderstood = false;
        //    }
        //    _mainConfig.Current.LastUidLoggedIn = "";
        //    _mainConfig.Save();
        //    _logger.LogInformation("Deleting Account from Server.");
        //    await _hub.UserDelete();
        //    DeleteAccountConfirmation = false;

        //    if (isPrimary)
        //    {
        //        var allFolders = Directory.GetDirectories(ConfigFileProvider.SundouleiaDirectory)
        //            .Where(c => !c.Contains("eventlog") && !c.Contains("audiofiles"))
        //            .ToList();

        //        foreach (var folder in allFolders)
        //            Directory.Delete(folder, true);

        //        _logger.LogInformation("Removed all deleted account folders.");
        //        _configFiles.ClearUidConfigs();
        //        await _hub.Disconnect(ServerState.Disconnected, false);
        //        _mediator.Publish(new SwitchToIntroUiMessage());
        //    }
        //    else
        //    {
        //        var accountProfileFolder = _configFiles.CurrentPlayerDirectory;
        //        if (Directory.Exists(accountProfileFolder))
        //        {
        //            _logger.LogDebug("Deleting Account Profile Folder for current character.", LoggerType.ApiCore);
        //            Directory.Delete(accountProfileFolder, true);
        //        }
        //        _configFiles.ClearUidConfigs();
        //        await _hub.Reconnect(false);
        //    }
        //}
        //catch (Bagagwa ex)
        //{
        //    _logger.LogError("Failed to delete account from server." + ex);
        //}
    }
}
