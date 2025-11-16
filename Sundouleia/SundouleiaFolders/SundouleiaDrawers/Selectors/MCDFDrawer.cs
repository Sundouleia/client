using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.Radar;

namespace Sundouleia.DrawSystem;

public class MCDFDrawer : DynamicDrawer<MCDFDummyData>
{
    private readonly SundesmoManager _sundesmos;

    public MCDFDrawer(ILogger<RadarDrawer> logger, SundesmoManager sundesmos, MCDFDrawSystem ds) 
        : base("##MCDF_Drawer", logger, ds)
    {
        _sundesmos = sundesmos;
        // We can handle interaction stuff via customizable buttons later that we will figure out as things go on.
    }

    // We can override every single component of the draw process here thanks to the dynamic drawer, it should be just a copy and paste from
    // the DynamicRadarFolder in our prototype model.

    // We can also add custom outputs for various button interactions, among other customizations.
    // Pretty much all parts of the draw process can be overridden.
}

