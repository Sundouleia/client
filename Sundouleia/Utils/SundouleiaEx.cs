using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.WebAPI;
using System.Runtime.CompilerServices;

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

    public static async Task WaitUntilFullyLoaded(IntPtr address, CancellationToken cts)
    {
        if (address == IntPtr.Zero)
            throw new ArgumentException("Address cannot be null.", nameof(address));

        while (!cts.IsCancellationRequested)
        {
            // Yes, our clients loading state also impacts anyone else's loading. (that or we are faster than dalamud's object table)
            if (!PlayerData.IsZoning && IsObjectLoaded(address))
                return;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     There are conditions where an object can be rendered / created, but not drawable, or currently bring drawn. <para />
    ///     This mainly occurs on login or when transferring between zones, but can also occur during redraws and such.
    ///     We can get around this by checking for various draw conditions.
    /// </summary>
    public unsafe static bool IsObjectLoaded(IntPtr gameObjectAddress)
    {
        var gameObj = (GameObject*)gameObjectAddress;
        // Invalid address.
        if (gameObjectAddress == IntPtr.Zero) return false;
        // DrawObject does not exist yet.
        if ((IntPtr)gameObj->DrawObject == IntPtr.Zero) return false;
        // RenderFlags are marked as 'still loading'.
        if ((ulong)gameObj->RenderFlags == 2048) return false;
        // There are models loaded into slots, still being applied.
        if (((CharacterBase*)gameObj->DrawObject)->HasModelInSlotLoaded != 0) return false;
        // There are model files loaded into slots, still being applied.
        if (((CharacterBase*)gameObj->DrawObject)->HasModelFilesInSlotLoaded != 0) return false;
        // Object is fully loaded.
        return true;
    }

    /// <summary>
    ///     There is a brief moment between when a player says they are interactable, 
    ///     and when they are actually available. <para />
    ///     This is a surefire way to know that we are ready for interaction. <para />
    /// </summary>
    public static unsafe bool IsScreenReady()
    {
        if (AddonHelp.TryGetAddonByName<AtkUnitBase>("NowLoading", out var a) && a->IsVisible) return false;
        if (AddonHelp.TryGetAddonByName<AtkUnitBase>("FadeMiddle", out var b) && b->IsVisible) return false;
        if (AddonHelp.TryGetAddonByName<AtkUnitBase>("FadeBack", out var c) && c->IsVisible) return false;
        return true;
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

    public static bool DrawFavoriteStar(FavoritesConfig config, FavoriteType type, Guid id, bool framed = true)
    {
        var isFavorite = type switch
        {
            FavoriteType.Status => FavoritesConfig.Statuses.Contains(id),
            FavoriteType.Preset => FavoritesConfig.Presets.Contains(id),
            _ => false
        };
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
            if (isFavorite) config.Unfavorite(type, id);
            else config.Favorite(type, id);
            return true;
        }
        return false;
    }

    public static bool DrawFavoriteStar(this FavoritesConfig config, string uid, bool framed)
    {
        var isFavorite = FavoritesConfig.SundesmoUids.Contains(uid);
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
            if (isFavorite) config.Unfavorite(uid);
            else config.Favorite(uid);
            return true;
        }
        return false;
    }

    public static bool DrawFavoriteStar(this FavoritesConfig config, uint iconId, bool framed)
    {
        var isFavorite = FavoritesConfig.IconIDs.Contains(iconId);
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
            if (isFavorite) config.Unfavorite(iconId);
            else config.Favorite(iconId);
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

    /// <summary>
    ///     Serializes and then deserializes object, returning result of deserialization using <see cref="Newtonsoft.Json"/>
    /// </summary>
    /// <returns> A Deep copy of <paramref name="obj"/></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T NewtonsoftDeepClone<T>(this T obj)
        => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj))!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            ServerState.Unattached => "Unattached",
            ServerState.NoSecretKey => "Invalid / No Secret Key",
            ServerState.Connected => "Connected",
            ServerState.ConnectedDataSynced => "Connected",
            _ => string.Empty
        };
    }
}
