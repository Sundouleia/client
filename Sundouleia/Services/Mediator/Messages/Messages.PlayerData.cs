using Sundouleia.Pairs;
using Sundouleia.WebAPI.Files.Models;

namespace Sundouleia.Services.Mediator;

// Sundesmo Management, and visibility handling.
public record SundesmoOnline(Sundesmo Sundesmo, bool NeedsFullData) : MessageBase;
public record SundesmoOffline(Sundesmo Sundesmo) : MessageBase;
public record SundesmoPlayerRendered(PlayerHandler Handler) : SameThreadMessage; // Effectively "becoming visible"
public record SundesmoEnteredLimbo(Sundesmo Sundesmo) : MessageBase; // Alteration Timeout Begin.
public record SundesmoLeftLimbo(Sundesmo Sundesmo) : MessageBase; // Alteration Timeout End.
public record TargetSundesmoMessage(Sundesmo Sundesmo) : MessageBase; // when desiring to target a sundesmo.
public record DownloadLimitChangedMessage : SameThreadMessage;
public record FileUploading(PlayerHandler Player) : MessageBase;
public record FileUploaded(PlayerHandler Player) : MessageBase;
public record FileDownloadStarted(PlayerHandler Player, ConcurrentDictionary<string, FileTransferProgress> Status) : MessageBase;
public record FileDownloadComplete(PlayerHandler Player) : MessageBase;

/// <summary>
///     Whenever a NON-CLIENT OWNED OBJECT is created. Intended for Sundesmos.
/// </summary>
public record WatchedObjectCreated(IntPtr Address) : SameThreadMessage;

/// <summary>
///     Whenever a NON-CLIENT OWNED OBJECT is destroyed. Intended for Sundesmos.
/// </summary>
public record WatchedObjectDestroyed(IntPtr Address) : SameThreadMessage;



