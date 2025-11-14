namespace Sundouleia.DrawSystem.Selector;

// Initialization and base calls for the selector.
public abstract class DummySelector<T> where T : class
{
    protected readonly ILogger Log;
    protected DynamicDrawSystem<T> DummySystem;

    private readonly string _label = string.Empty;

    public DummySelector(string label, ILogger log, DynamicDrawSystem<T> drawSystem)
    {
        _label = label;
        Log = log;
        DummySystem = drawSystem;
    }

    public string Label
    {
        get => _label;
        init
        {
            _label = value;
        }
    }
}

// Reminder that a cache is not responsible for ensuring validity of a folder, rather it ensures
// that the searched filter remains up to date.
public class DummyDrawFilterCache<T> where T : class
{
    private readonly DynamicDrawSystem<T> _drawSystem;
    private 


    public DummyDrawCache(DynamicDrawSystem<T> drawSystem)
    {
        _drawSystem = drawSystem;
    }

    public struct CachedHierarchy
    {
        public IDynamicFolderNode Folder;
        public IDynamicNode[]? Children;
    }
}