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

    // Finish later.
}

