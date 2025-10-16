using CkCommons.Chat;
using SundouleiaAPI.Data;

namespace Sundouleia.Radar.Chat;
public record RadarCkChatMessage(UserData UserData, string Name, string Message) 
    : CkChatMessage(Name, Message, DateTime.UtcNow)
{
    public override string UID => UserData.UID ?? base.UID;
    public CkVanityTier Tier => UserData.Tier ?? CkVanityTier.NoRole;
}
