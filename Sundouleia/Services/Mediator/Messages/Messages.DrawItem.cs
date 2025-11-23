namespace Sundouleia.Services.Mediator;

// Subscriptions for Group changes, different changes imply different implications.

// Recalculate whenever entities are added / removed.
public record FolderUpdateSundesmos : MessageBase;
public record FolderUpdateGroups : MessageBase;
public record FolderUpdateRadar : MessageBase;
public record FolderUpdateRequests : MessageBase;

// Resort without doing recalculations.
public record FolderSortSundesmos : MessageBase;
public record FolderSortGroups : MessageBase;
public record FolderSortRadar : MessageBase;
public record FolderSortRequests : MessageBase;