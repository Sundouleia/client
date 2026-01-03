namespace Sundouleia.Services.Mediator;

// Subscriptions for Group changes, different changes imply different implications.

// Recalculate whenever entities are added / removed.
public record FolderUpdateSundesmos : MessageBase;
public record FolderUpdateGroups : MessageBase;
public record FolderUpdateRadar : MessageBase;
public record FolderUpdateRequests : MessageBase;
