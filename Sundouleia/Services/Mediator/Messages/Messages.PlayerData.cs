using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.WebAPI.Files.Models;
using SundouleiaAPI.Data;

namespace Sundouleia.Services.Mediator;

// Sundesmo Management, and visibility handling.
public record SundesmoOnline(Sundesmo Sundesmo, bool RemoveFromLimbo) : MessageBase;
public record SundesmoOffline(Sundesmo Sundesmo) : MessageBase;
public record SundesmoPlayerRendered(PlayerHandler Handler) : SameThreadMessage; // Effectively "becoming visible"
public record SundesmoEnteredLimbo(Sundesmo Sundesmo) : MessageBase; // Alteration Timeout Begin.
public record SundesmoLeftLimbo(Sundesmo Sundesmo) : MessageBase; // Alteration Timeout End.
public record TargetSundesmoMessage(Sundesmo Sundesmo) : MessageBase; // when desiring to target a sundesmo.
public record SendTempRequestMessage(UserData TargetUser) : MessageBase; // for examine-based sends. Not sends handled via UI.
public record DownloadLimitChangedMessage : SameThreadMessage;
public record FilesUploading(PlayerHandler Player) : MessageBase;
public record FilesUploaded(PlayerHandler Player) : MessageBase;
public record FileDownloadStarted(PlayerHandler Player, FileTransferProgress Status) : MessageBase;
public record FileDownloadComplete(PlayerHandler Player) : MessageBase;

/// <summary>
///     Whenever the ModdedStateManager finished calculating the state 
///     of our current client-owned actors. <para />
///     Note that this currently does not take into account 
///     100 percent accurate transients but can after new penumbra API.
/// </summary>
public record ModdedStateCollected(ModdedState ModdedState) : MessageBase;

/// <summary>
///     Whenever a CLIENT OWNED OBJECT is created.
/// </summary>
public record OwnedObjectCreated(OwnedObject Kind, IntPtr Address) : SameThreadMessage;

/// <summary>
///     Whenever a CLIENT OWNED OBJECT is destroyed.
/// </summary>
public record OwnedObjectDestroyed(OwnedObject Kind, IntPtr Address) : SameThreadMessage;

/// <summary>
///     Whenever a NON-CLIENT OWNED OBJECT is created. Intended for Sundesmos.
/// </summary>
public record WatchedObjectCreated(IntPtr Address) : SameThreadMessage;

/// <summary>
///     Whenever a NON-CLIENT OWNED OBJECT is destroyed. Intended for Sundesmos.
/// </summary>
public record WatchedObjectDestroyed(IntPtr Address) : SameThreadMessage;



