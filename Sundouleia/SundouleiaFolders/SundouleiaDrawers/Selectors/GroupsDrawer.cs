using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;

namespace Sundouleia.DrawSystem;

public class GroupsDrawer : DynamicDrawer<Sundesmo>
{
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;

    public GroupsDrawer(ILogger<GroupsDrawer> logger, GroupsManager groups, 
        SundesmoManager sundesmos, GroupsDrawSystem ds)
        : base("##GroupsDrawer", logger, ds)
    {
        _groups = groups;
        _sundesmos = sundesmos;
    }

    // Finish later.
}


