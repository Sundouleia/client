using Sundouleia.Loci.Data;
using Sundouleia.PlayerClient;

namespace Sundouleia.Services.Mediator;

// Subscriptions for Group changes, different changes imply different implications.

// Recalculate whenever entities are added / removed.
public record FolderUpdateSundesmos : MessageBase;
public record FolderUpdateGroups : MessageBase;
public record FolderUpdateGroup(string GroupName) : MessageBase;
public record FolderUpdateRadar : MessageBase;
public record FolderUpdateRequests : MessageBase;

// CKFS
public enum FSChangeType { Created, Deleted, Renamed, Modified }

public record LociStatusChanged(FSChangeType Type, LociStatus Item, string? OldString = null) : MessageBase;
public record LociPresetChanged(FSChangeType Type, LociPreset Item, string? OldString = null) : MessageBase;
public record ReloadCKFS(bool IsPresetFS) : MessageBase;