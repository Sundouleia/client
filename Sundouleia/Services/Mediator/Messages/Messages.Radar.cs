using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace Sundouleia.Services.Mediator;

/// <summary>
///     A Config option related to Radar State was changed.
/// </summary>
public record RadarConfigChanged(string OptionName) : MessageBase;

/// <summary>
///     For sending Radar Chats. Can be possibly moved out of mediator.
/// </summary>
public record NewRadarChatMessage(RadarChatMessage Message, bool FromSelf) : MessageBase;

/// <summary>
///     User was either added, or has a state update.
/// </summary>
public record RadarAddOrUpdateUser(OnlineUser UpdatedUser) : MessageBase;

/// <summary>
///     Radar User should be removed.
/// </summary>
/// <param name="User"></param>
public record RadarRemoveUser(UserData User) : MessageBase;

/// <summary>
///     Whenever the territory updates.
/// </summary>
public record RadarTerritoryChanged(ushort PrevTerritory, ushort NewTerritory) : MessageBase;