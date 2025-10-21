using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace Sundouleia.PlayerClient;

/// <summary>
///     Information on a current request that you have pending for another user.
/// </summary>
public class PendingRequest
{
    private UserData _target;
    private DateTime _createdAt;

    public PendingRequest(UserData target)
    {
        _target = target;
        _createdAt = DateTime.UtcNow;
    }

    public PendingRequest(SundesmoRequest existing)
    {
        _target = existing.Target;
        _createdAt = existing.CreatedAt;
        IsTemporary = existing.Details.IsTemp;
        Message = existing.Details.Message;
        SentFromWorldId = existing.Details.FromWorldId;
        SentFromZoneId = existing.Details.FromZoneId;
    }

    public string Message { get; private set; } = string.Empty;
    public bool IsTemporary { get; private set; } = false;
    // Useful for [Accept all in zone] or whatever.
    public ushort SentFromWorldId { get; private set; } = 0;
    public ushort SentFromZoneId { get; private set; } = 0;

    public string TargetUID => _target.UID;
    public string TargetDisplayName => _target.AnonName;

    public TimeSpan TimeLeft() => TimeSpan.FromDays(3) - (DateTime.UtcNow - _createdAt);
    public bool IsExpired() => DateTime.UtcNow - _createdAt > TimeSpan.FromDays(3);

    public SundesmoRequest ToDto()
        => new(User: MainHub.OwnUserData, _target, new(IsTemporary, Message, SentFromWorldId, SentFromZoneId), _createdAt);
}