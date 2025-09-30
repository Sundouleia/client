using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Handlers;
using SundouleiaAPI.Data;

namespace Sundouleia.Services.Mediator;

// Sundesmo Management
public record PairWentOnlineMessage(UserData UserData) : MessageBase; // a message indicating a pair has gone online.
public record PairHandlerVisibleMessage(SundesmoHandler Player) : MessageBase; // a message indicating the visibility of a pair handler.
public record PairWasRemovedMessage(UserData UserData) : MessageBase; // a message indicating a pair has been removed.
public record TargetPairMessage(Sundesmo Pair) : MessageBase; // called when publishing a targeted pair connection (see UI)


public record FileUploading(SundesmoHandler Player) : MessageBase;
public record FileUploaded(SundesmoHandler Player) : MessageBase;
public record FileDownloadReady(Guid requestId) : MessageBase; // Maybe remove this.
public record FileDownloadStarted(SundesmoHandler Player, Dictionary<string, string> Status) : MessageBase;
public record FileDownloadComplete(SundesmoHandler Player) : MessageBase;

public record CharacterObjectCreated(IntPtr Address) : SameThreadMessage;
public record CharacterObjectDestroyed(IntPtr Address) : SameThreadMessage;
public record OwnedCharaCreated(OwnedObject Kind, IntPtr Address) : SameThreadMessage;
public record OwnedObjectDestroyed(OwnedObject Kind, IntPtr Address) : SameThreadMessage;



