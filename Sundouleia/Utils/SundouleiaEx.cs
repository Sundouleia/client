using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia.PlayerClient;
using Sundouleia.WebAPI;

namespace Sundouleia;
public static class SundouleiaEx
{
    public static bool HasValidSetup(this MainConfig config)
        => config.Current.AcknowledgementUnderstood;
    public static bool HasValidSetup(this AccountConfig config)
        => config.Current.Authentications.Count > 0;

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
