using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.WebAPI;

namespace Sundouleia;
public static class SundouleiaEx
{
    /// <summary>
    ///     A reliable Player.Interactable, that also waits on the loading screen to finish. <para />
    ///     Useful when waiting on player loading for UI manipulation and interactions.
    /// </summary>
    public static async Task WaitForPlayerLoading()
    {
        while (!await Svc.Framework.RunOnFrameworkThread(IsPlayerFullyLoaded).ConfigureAwait(false))
        {
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    public static bool  IsPlayerFullyLoaded()
        => PlayerData.Interactable && IsScreenReady();

    /// <summary>
    ///     There is a brief moment between when a player says they are interactable, 
    ///     and when they are actually available. <para />
    ///     This is a surefire way to know that we are ready for interaction. <para />
    /// </summary>
    public static unsafe bool IsScreenReady()
    {
        if (TryGetAddonByName<AtkUnitBase>("NowLoading", out var a) && a->IsVisible) return false;
        if (TryGetAddonByName<AtkUnitBase>("FadeMiddle", out var b) && b->IsVisible) return false;
        if (TryGetAddonByName<AtkUnitBase>("FadeBack", out var c) && c->IsVisible) return false;
        return true;
    }

    /// <summary>
    ///     Obtain an addon* by its name alone. If it is not found, returns false.
    /// </summary>
    public static unsafe bool TryGetAddonByName<T>(string addon, out T* addonPtr) where T : unmanaged
    {
        // we can use a more direct approach now that we have access to static classes.
        var a = Svc.GameGui.GetAddonByName(addon, 1);
        if (a == IntPtr.Zero)
        {
            addonPtr = null;
            return false;
        }
        else
        {
            addonPtr = (T*)a.Address;
            return true;
        }
    }

    // Can make this internal probably.
    public static bool HasValidSetup(this MainConfig config)
        => config.Current.AcknowledgementUnderstood;

    public static bool HasValidCacheFolderSetup(this MainConfig config)
        => config.Current.InitialScanComplete
        && !string.IsNullOrEmpty(config.Current.CacheFolder)
        && Directory.Exists(config.Current.CacheFolder);

    public static bool HasValidExportFolderSetup(this MainConfig config)
        => !string.IsNullOrEmpty(config.Current.SMAExportFolder)
        && Directory.Exists(config.Current.SMAExportFolder);

    public static bool HasValidSMACache(this MainConfig config)
        => !string.IsNullOrEmpty(config.Current.CacheFolder) && Directory.Exists(config.Current.SMACacheFolder);

    public static bool DrawFavoriteStar(this FavoritesConfig config, string uid, bool framed)
    {
        var isFavorite = config.SundesmoUids.Contains(uid);
        var pos = ImGui.GetCursorScreenPos();
        var hovering = ImGui.IsMouseHoveringRect(pos, pos + new Vector2(ImGui.GetTextLineHeight()));
        var col = hovering ? ImGuiColors.DalamudGrey2 : isFavorite ? ImGuiColors.ParsedGold : ImGuiColors.ParsedGrey;

        if (framed)
            CkGui.FramedIconText(FAI.Star, col);
        else
            CkGui.IconText(FAI.Star, col);
        CkGui.AttachToolTip((isFavorite ? "Remove" : "Add") + " from Favorites.");

        if (hovering && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (isFavorite) config.RemoveUser(uid);
            else config.TryAddUser(uid);
            return true;
        }
        return false;
    }

    // Could maybe optimize better with button generation like we do
    // in our connection states, but as always can optimize this later.
    public static bool DrawTempUserLink(Sundesmo sundesmo, bool disabled)
    {
        var pos = ImGui.GetCursorScreenPos();
        var col = ImGuiColors.ParsedGrey; // Default color state.
        var shiftDown = ImGui.GetIO().KeyShift;
        var hovering = false;
        if (!disabled)
        {
            hovering = ImGui.IsMouseHoveringRect(pos, pos + new Vector2(ImGui.GetFrameHeight()));
            var pressed = hovering && shiftDown && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            col = pressed ? ImGuiColors.TankBlue : hovering ? ImGuiColors.DalamudGrey2 : ImGuiColors.ParsedGrey;
        }

        CkGui.FramedIconText(FAI.History, col);
        CkGui.AttachToolTip($"Your pairing with {sundesmo.GetDisplayName()} is Temporary." +
            $"--SEP----COL--[SHIFT + L-Click]--COL--Convert to permanent.", ImGuiColors.TankBlue);

        return hovering && shiftDown && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
    }

    public static string ByteToString(long bytes, bool addSuffix = true)
    {
        string[] suffix = ["B", "KiB", "MiB", "GiB", "TiB"];
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return addSuffix ? $"{dblSByte:0.00} {suffix[i]}" : $"{dblSByte:0.00}";
    }

    public static bool IsSingleFlagSet(byte value)
        => value != 0 && (value & (value - 1)) == 0;

    // May want to move to a 'toName' file or something.
    public static string ToName(this InteractionFilter filterKind)
        => filterKind switch
        {
            InteractionFilter.All => "All",
            InteractionFilter.Applier => "Nick/Alias/UID",
            InteractionFilter.Interaction => "Interaction Type",
            InteractionFilter.Content => "Content Details",
            _ => "UNK"
        };

    public static T DeepClone<T>(this T obj)
        => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(obj))!;

    /// <summary> 
    ///     Retrieves the various UID text based on the current server state.
    /// </summary>
    public static string GetErrorText()
    {
        return MainHub.ServerStatus switch
        {
            ServerState.Reconnecting => "Reconnecting",
            ServerState.Connecting => "Connecting",
            ServerState.Disconnected => "Disconnected",
            ServerState.Disconnecting => "Disconnecting",
            ServerState.Unauthorized => "Unauthorized",
            ServerState.VersionMisMatch => "Version mismatch",
            ServerState.Offline => "Unavailable",
            ServerState.NoSecretKey => "Invalid / No Secret Key",
            ServerState.Connected => MainHub.DisplayName,
            ServerState.ConnectedDataSynced => MainHub.DisplayName,
            _ => string.Empty
        };
    }
}
