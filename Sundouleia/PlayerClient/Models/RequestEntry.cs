using Sundouleia.WebAPI;
using SundouleiaAPI.Network;

namespace Sundouleia.PlayerClient;

/// <summary>
///     Information on a current request that you have pending for another user.
/// </summary>
public class RequestEntry(SundesmoRequest request) : IEquatable<RequestEntry>, IEquatable<SundesmoRequest>
{
    public bool FromClient => request.User.UID == MainHub.OwnUserData.UID;

    // For anonymous display.
    public string SenderAnonName => request.User.AnonName;
    public string SenderTag => request.User.AnonTag;
    public string RecipientAnonName => request.Target.AnonName;
    public string RecipientTag => request.Target.AnonTag;

    // For comparison and unique identification.
    public string SenderUID => request.User.UID;
    public string RecipientUID => request.Target.UID;

    // Information about said request.
    public bool IsTemporaryRequest => request.Details.IsTemp;
    public string Message => request.Details.Message;

    public bool HasMessage => request.Details.Message.Length > 0;
    public bool HasExpired => request.IsExpired();
    public TimeSpan TimeToRespond => request.TimeLeft();
    public DateTime SentTime => request.CreatedAt;
    public DateTime ExpireTime => request.CreatedAt + TimeSpan.FromDays(3);

    // Helpers.
    public bool SentFromCurrentArea(ushort worldId, ushort zoneId)
        => request.Details.FromWorldId == worldId && request.Details.FromZoneId == zoneId;

    public bool SentFromWorld(ushort worldId)
        => request.Details.FromWorldId == worldId;

    public string GetRemainingTimeString()
    {
        var timeLeft = TimeToRespond;
        return timeLeft.Days > 0 ? $"{timeLeft.Days}d {timeLeft.Hours}h" : $"{timeLeft.Hours}h {timeLeft.Minutes}m";
    }

    // Equality members.
    public bool Equals(RequestEntry? other)
        => other is not null &&
           SenderUID == other.SenderUID &&
           RecipientUID == other.RecipientUID;

    public bool Equals(SundesmoRequest? other)
        => other is not null &&
           SenderUID == other.User.UID &&
           RecipientUID == other.Target.UID;

    public override bool Equals(object? obj)
        => obj switch
        {
            RequestEntry re => Equals(re),
            SundesmoRequest req => Equals(req),
            _ => false
        };

    public override int GetHashCode()
        => HashCode.Combine(SenderUID, RecipientUID);

    public static bool operator ==(RequestEntry? left, RequestEntry? right)
        => Equals(left, right);

    public static bool operator !=(RequestEntry? left, RequestEntry? right)
        => !Equals(left, right);
}