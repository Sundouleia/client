using CkCommons.FileSystem;
using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using OtterGui;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;

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
        // We can handle interaction stuff via customizable buttons later that we will figure out as things go on.
    }

    // We can override every single component of the draw process here thanks to the dynamic drawer, it should be just a copy and paste from
    // the DynamicRadarFolder in our prototype model.

    // We can also add custom outputs for various button interactions, among other customizations.
    // Pretty much all parts of the draw process can be overridden.
}

