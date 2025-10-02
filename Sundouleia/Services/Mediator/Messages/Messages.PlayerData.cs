using Sundouleia.Pairs;
using SundouleiaAPI.Data;

namespace Sundouleia.Services.Mediator;

// Sundesmo Management
public record PairWentOnlineMessage(UserData UserData) : MessageBase; // a message indicating a pair has gone online.
public record PairHandlerVisibleMessage(PlayerHandler Player) : MessageBase; // a message indicating the visibility of a pair handler.
public record PairWasRemovedMessage(UserData UserData) : MessageBase; // a message indicating a pair has been removed.
public record TargetSundesmoMessage(Sundesmo Sundesmo) : MessageBase; // called when publishing a targeted pair connection (see UI)


public record DownloadLimitChangedMessage : SameThreadMessage;
public record FileUploading(PlayerHandler Player) : MessageBase;
public record FileUploaded(PlayerHandler Player) : MessageBase;
public record FileDownloadReady(Guid RequestId) : MessageBase; // Maybe remove this.
public record FileDownloadStarted(PlayerHandler Player, Dictionary<string, string> Status) : MessageBase;
public record FileDownloadComplete(PlayerHandler Player) : MessageBase;

public record WatchedObjectCreated(OwnedObject Kind, IntPtr Address) : SameThreadMessage;
public record WatchedObjectDestroyed(OwnedObject Kind, IntPtr Address) : SameThreadMessage;



