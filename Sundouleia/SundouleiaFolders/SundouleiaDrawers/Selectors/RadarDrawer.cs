using CkCommons.FileSystem;
using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using OtterGui;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.Radar;

namespace Sundouleia.DrawSystem;

public class RadarDrawer : DynamicDrawer<RadarUser>
{
    private readonly RadarManager _manager;
    private readonly SundesmoManager _sundesmos;

    public RadarDrawer(ILogger<RadarDrawer> logger, RadarManager manager, 
        SundesmoManager sundesmos, RadarDrawSystem ds) 
        : base("##RadarDrawer", logger, ds)
    {
        _manager = manager;
        _sundesmos = sundesmos;
        // We can handle interaction stuff via customizable buttons later that we will figure out as things go on.
    }

    // We can override every single component of the draw process here thanks to the dynamic drawer, it should be just a copy and paste from
    // the DynamicRadarFolder in our prototype model.

    // We can also add custom outputs for various button interactions, among other customizations.
    // Pretty much all parts of the draw process can be overridden.
}

