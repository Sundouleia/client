using Sundouleia.Radar.Chat;
using SundouleiaAPI.Network;

namespace Sundouleia.Services.Mediator;

// Dunno why we need this anymore.
public record RadarConfigChanged(string OptionName) : MessageBase;

// I guess we could use this but may be better to call directly?
public record NewRadarChatMessage(LoggedRadarChatMessage Message) : MessageBase;