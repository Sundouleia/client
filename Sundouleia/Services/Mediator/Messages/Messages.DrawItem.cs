using Sundouleia.PlayerClient;

namespace Sundouleia.Services.Mediator;

// Subscriptions for Group changes, different changes imply different implications.

// Recalculate whenever entities are added / removed.
public record FolderUpdateSundesmos : MessageBase;
public record FolderUpdateGroups : MessageBase;
public record FolderUpdateGroup(string GroupName) : MessageBase;
public record FolderUpdateRadar : MessageBase;
public record FolderUpdateRequests : MessageBase;

// Update helpers for DTR and stuff.
public record NewRequestAdded(RequestEntry request) : MessageBase;
