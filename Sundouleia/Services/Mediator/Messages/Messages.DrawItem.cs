namespace Sundouleia.Services.Mediator;

// Not currently the best implementation of this and could be improved overtime.
// Currently the messy part of these folders is there dependency on mediators for updates,
// over some way to listen to changes as events or actions.

// This can lead to a condition where making a single mediator call for a generic update
// could cause a race condition if called in quick succession, and is why we need to depend
// on separate calls.

// Down the line, I would hope to implement calls for additions and removals, as a list in c#,
// nonmatter what object is in it, is only a pointer reference to that object. 

// Obviously it wont be a big deal to just store one list of all the draw entities for all objects,
// and then take the ones that match the filter.
// However, that also has its own considerations too.

// ---- Summary of current messages ----
// The main reason we want to not recreate all entities on each search update is that any interactions
// done on an object internally will be reset every update.
// If we had some intermediate handler to process this, we could avoid all of this mess entirely.
// (and is something I would like to add down the line as it will allow us to expand upon this more and be more versitile.)

/// <summary> Full Folder Reset. </summary>
public record RegenerateAll : MessageBase;

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