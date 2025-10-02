using Sundouleia.Pairs;

namespace Sundouleia.Services.Mediator;

// Useful for IPC-Updates, but may also become irrelevant once we get proper watchers / services / listeners hooked up for changes.
public record PenumbraInitialized : MessageBase;
public record PenumbraDirectoryChanged(string? NewDirectory) : MessageBase;
public record PenumbraSettingsChanged : SameThreadMessage;
public record PenumbraResourceLoaded(IntPtr Address, string GamePath, string ReplacePath) : SameThreadMessage;
public record PenumbraDisposed : MessageBase;

public record HaltFileScan(string Source) : MessageBase;
public record ResumeFileScan(string Source) : MessageBase;

public record GlamourerChanged(nint address) : MessageBase; // Only sent for CLIENT Glamourer changes

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
public record MoodlesChanged(nint address) : MessageBase;

// Should be careful how we use this and should likely change it after adding the VisibleUsersMonitor
public record VisibleUsersChanged : MessageBase; // for pinging the moodles.
public record MoodlesPermissionsUpdated(Sundesmo User) : MessageBase;
