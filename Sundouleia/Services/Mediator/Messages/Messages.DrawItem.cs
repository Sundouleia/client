using Sundouleia.Pairs;
using Sundouleia.PlayerClient;

namespace Sundouleia.Services.Mediator;

// All messages related to DrawEntities and DrawFolders for UI management.
// These via updates to SundesmoManager changes, RequestManager updates, or GroupManager changes.
//
// We specifically design the folders in such a way where their final states are not immutable,
// such that we can add / remove the draw entities over recreating them, to ensure that any being interacted
// with are not de-referenced or linger in memory unnecessarily.

public enum RefreshTarget
{
    Sundesmos,
    Groups,
    Radar,
    Requests,
}

/// <summary> For full Regeneration </summary>
public record RegenerateEntries(RefreshTarget TargetFolders) : MessageBase;

public record GroupAdded(SundesmoGroup Group) : MessageBase;
public record GroupRemoved(SundesmoGroup Group) : MessageBase;