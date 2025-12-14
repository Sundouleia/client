using Sundouleia.Pairs;
using SundouleiaAPI.Network;

namespace Sundouleia.Services.Mediator;

// Useful for IPC-Updates, but may also become irrelevant once we get proper watchers / services / listeners hooked up for changes.
public record PenumbraInitialized : MessageBase;
public record PenumbraDirectoryChanged(string? NewDirectory) : MessageBase;
public record PenumbraObjectRedrawn(IntPtr Address, int ObjectIdx) : SameThreadMessage;
public record PenumbraDisposed : MessageBase;

public record GlamourerChanged(IntPtr Address) : MessageBase; // Only sent for CLIENT Glamourer changes

public record CustomizeReady : MessageBase;
public record CustomizeProfileChange(IntPtr Address, Guid Id) : MessageBase;
public record CustomizeProfileListRequest : MessageBase;
public record CustomizeDisposed : MessageBase;

public record HeelsOffsetChanged : MessageBase; // Whenever the client's Heel offset changes.

public record HonorificReady : MessageBase;
public record HonorificTitleChanged(string NewTitle) : MessageBase;

public record PetNamesReady : MessageBase;
public record PetNamesDataChanged(string NicknamesData) : MessageBase;

public record MoodlesReady : MessageBase;
public record MoodlesChanged(IntPtr Address) : MessageBase;
// For Moodle PairAccess changes in permissions
public record MoodleAccessPermsChanged(Sundesmo Sundesmo) : MessageBase;
public record MoodlesApplyStatusToPair(ApplyMoodleStatus ApplyStatusTupleDto) : MessageBase;


public record ModelRelatedResourceLoaded(OwnedObject Object) : MessageBase;
public record TransientResourceLoaded(OwnedObject Object) : MessageBase;
