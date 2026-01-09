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