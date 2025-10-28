using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Component.GUI;
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

    public static bool IsPlayerFullyLoaded()
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

    public static bool HasValidSetup(this MainConfig config)
        => config.Current.AcknowledgementUnderstood;

    public static bool HasValidCacheFolderSetup(this MainConfig config)
        => config.Current.InitialScanComplete
        && !string.IsNullOrEmpty(config.Current.CacheFolder)
        && Directory.Exists(config.Current.CacheFolder);
    public static bool HasValidSetup(this AccountConfig config)
        => config.Current.LoginAuths.Count is not 0 && config.Current.Profiles.Count is not 0;

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

    /// <summary> Linearly interpolates between two values based on a factor t. </summary>
    /// <remarks> Think, “What number is 35% between 56 and 132?" </remarks>
    /// <param name="a"> lower bound value </param>
    /// <param name="b"> upper bound value </param>
    /// <param name="t"> should be in the range [a, b] </param>
    /// <returns> the interpolated value between a and b </returns>
    public static float Lerp(float a, float b, float t) 
        => a + (b - a) * t;

    public static float EaseInExpo(float t) 
        => t <= 0f ? 0f : MathF.Pow(2f, 10f * (t - 1f));

    public static float EaseOutExpo(float t)
        => t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t);

    public static float EaseInOutSine(float t)
        => (1f - MathF.Cos(t * MathF.PI)) * 0.5f;

    public static Vector4 UidColor()
    {
        return MainHub.ServerStatus switch
        {
            ServerState.Connecting => ImGuiColors.DalamudYellow,
            ServerState.Reconnecting => ImGuiColors.DalamudRed,
            ServerState.Connected => ImGuiColors.ParsedPink,
            ServerState.ConnectedDataSynced => ImGuiColors.ParsedPink,
            ServerState.Disconnected => ImGuiColors.DalamudYellow,
            ServerState.Disconnecting => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => ImGuiColors.DalamudRed,
            ServerState.VersionMisMatch => ImGuiColors.DalamudRed,
            ServerState.Offline => ImGuiColors.DalamudRed,
            ServerState.NoSecretKey => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudRed
        };
    }

    public static Vector4 ServerStateColor()
    {
        return MainHub.ServerStatus switch
        {
            ServerState.Connecting => ImGuiColors.DalamudYellow,
            ServerState.Reconnecting => ImGuiColors.DalamudYellow,
            ServerState.Connected => ImGuiColors.HealerGreen,
            ServerState.ConnectedDataSynced => ImGuiColors.HealerGreen,
            ServerState.Disconnected => ImGuiColors.DalamudRed,
            ServerState.Disconnecting => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => ImGuiColors.ParsedOrange,
            ServerState.VersionMisMatch => ImGuiColors.ParsedOrange,
            ServerState.Offline => ImGuiColors.DPSRed,
            ServerState.NoSecretKey => ImGuiColors.ParsedOrange,
            _ => ImGuiColors.ParsedOrange
        };
    }

    public static FAI ServerStateIcon(ServerState state)
    {
        return state switch
        {
            ServerState.Connecting => FAI.SatelliteDish,
            ServerState.Reconnecting => FAI.SatelliteDish,
            ServerState.Connected => FAI.Link,
            ServerState.ConnectedDataSynced => FAI.Link,
            ServerState.Disconnected => FAI.Unlink,
            ServerState.Disconnecting => FAI.SatelliteDish,
            ServerState.Unauthorized => FAI.Shield,
            ServerState.VersionMisMatch => FAI.Unlink,
            ServerState.Offline => FAI.Signal,
            ServerState.NoSecretKey => FAI.Key,
            _ => FAI.ExclamationTriangle
        };
    }

    /// <summary> 
    ///     Retrieves the various UID text based on the current server state.
    /// </summary>
    public static string GetUidText()
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
            ServerState.NoSecretKey => "No Secret Key",
            ServerState.Connected => MainHub.DisplayName,
            ServerState.ConnectedDataSynced => MainHub.DisplayName,
            _ => string.Empty
        };
    }
}
