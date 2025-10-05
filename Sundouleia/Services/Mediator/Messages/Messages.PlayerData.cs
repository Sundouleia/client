using Sundouleia.Pairs;
using SundouleiaAPI.Data;

namespace Sundouleia.Services.Mediator;

// Sundesmo Management, and visibility handling.
public record SundesmoOnline(Sundesmo Sundesmo, bool AlreadyRendered) : MessageBase;
public record SundesmoOffline(Sundesmo Sundesmo) : MessageBase;
public record SundesmoPlayerRendered(PlayerHandler Handler) : SameThreadMessage; // Effectively "becoming visible"
public record SundesmoPlayerUnrendered(PlayerHandler Handler) : SameThreadMessage; // Effectively "becoming invisible"
public record SundesmoTimedOut(PlayerHandler Handler) : MessageBase; // Whenever unrendered long enough to be considered invalid.

public record TargetSundesmoMessage(Sundesmo Sundesmo) : MessageBase; // called when publishing a targeted pair connection (see UI)


public record DownloadLimitChangedMessage : SameThreadMessage;
public record FileUploading(PlayerHandler Player) : MessageBase;
public record FileUploaded(PlayerHandler Player) : MessageBase;
public record FileDownloadReady(Guid RequestId) : MessageBase; // Maybe remove this.
public record FileDownloadStarted(PlayerHandler Player, Dictionary<string, string> Status) : MessageBase;
public record FileDownloadComplete(PlayerHandler Player) : MessageBase;

public record WatchedObjectCreated(OwnedObject Kind, IntPtr Address) : SameThreadMessage;
public record WatchedObjectDestroyed(OwnedObject Kind, IntPtr Address) : SameThreadMessage;



