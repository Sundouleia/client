using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;

namespace Sundouleia.DrawSystem;

public class WhitelistDrawer : DynamicDrawer<Sundesmo>
{
    private readonly SundesmoManager _sundesmos;

    public WhitelistDrawer(ILogger<WhitelistDrawer> logger, SundesmoManager sundesmos, 
        WhitelistDrawSystem ds) 
        : base("##WhitelistDrawer", logger, ds)
    {
        _sundesmos = sundesmos;
        // We can handle interaction stuff via customizable buttons later that we will figure out as things go on.
    }


    // We can override every single component of the draw process here thanks to the dynamic drawer, it should be just a copy and paste from
    // the DynamicRadarFolder in our prototype model.

    // We can also add custom outputs for various button interactions, among other customizations.
    // Pretty much all parts of the draw process can be overridden.
}

