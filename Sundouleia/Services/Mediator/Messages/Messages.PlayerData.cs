using Sundouleia.Pairs;
using Sundouleia.Pairs.Handlers;
using SundouleiaAPI.Data;

namespace Sundouleia.Services.Mediator;

// Sundesmo Management
public record PairWentOnlineMessage(UserData UserData) : MessageBase; // a message indicating a pair has gone online.
public record PairHandlerVisibleMessage(SundesmoHandler Player) : MessageBase; // a message indicating the visibility of a pair handler.
public record PairWasRemovedMessage(UserData UserData) : MessageBase; // a message indicating a pair has been removed.
public record TargetPairMessage(Sundesmo Pair) : MessageBase; // called when publishing a targeted pair connection (see UI)

// Should modify this to go into a player pointer manager or something since it is a fundamental and frequently checked thing.
public record UserGameObjCreatedMessage(UserGameObj UserGameObj) : MessageBase;
public record UserGameObjDestroyedMessage(UserGameObj UserGameObj) : MessageBase;

public record OwnedObjectCreated(OwnedObject Kind, IntPtr Address) : SameThreadMessage;
public record OwnedObjectDestroyed(OwnedObject Kind, IntPtr Address) : SameThreadMessage;



