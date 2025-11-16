using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;

namespace Sundouleia.DrawSystem;

public class RequestsDrawer : DynamicDrawer<RequestEntry>
{
    private readonly RequestsManager _manager;
    private readonly SundesmoManager _sundesmos;

    public RequestsDrawer(ILogger<RadarDrawer> logger, RequestsManager manager, 
        SundesmoManager sundesmos, RequestsDrawSystem ds) 
        : base("##RequestsDrawer", logger, ds)
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

