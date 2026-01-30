using Sundouleia.Pairs.Enums;
using Sundouleia.PlayerClient;

namespace Sundouleia;

// We handle this through individual cases because its more efficient 
public static class FlagEx
{
    public static bool HasAny(this RedrawKind flags, RedrawKind check) => (flags & check) != 0;
    public static bool HasAny(this RequestAlertKind flags, RequestAlertKind check) => (flags & check) != 0;
}
