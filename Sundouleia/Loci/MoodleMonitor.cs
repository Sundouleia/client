//using CkCommons;
//using CkCommons.Helpers;
//using CkCommons.HybridSaver;
//using FFXIVClientStructs.FFXIV.Client.Game.Character;
//using FFXIVClientStructs.FFXIV.Client.Game.Object;
//using Sundouleia.Interop;
//using Sundouleia.Loci;
//using Sundouleia.Loci.Data;
//using Sundouleia.PlayerClient;
//using Sundouleia.Services.Configs;
//using Sundouleia.Services.Mediator;
//using Sundouleia.Watchers;

//namespace Sundouleia.Pairs;

//public class MoodleActorInfo
//{
//    public static readonly MoodleActorInfo Empty = new MoodleActorInfo();

//    public List<MoodlesStatusInfo> StatusInfos = [];
//    public HashSet<Guid> Ids = [];
//    public int Count => Ids.Count;
//}

///// <summary>
/////     Monitor the state of actors in Moodles to prevent overwrite
///// </summary>
//public sealed class MoodleMonitor : DisposableMediatorSubscriberBase
//{
//    private readonly IpcCallerMoodles _moodles;

//    private static Dictionary<nint, MoodleActorInfo> _actorData = new();

//    public MoodleMonitor(ILogger<MoodleMonitor> logger, SundouleiaMediator mediator, IpcCallerMoodles moodles)
//        : base(logger, mediator)
//    {
//        _moodles = moodles;
//        Mediator.Subscribe<MoodlesDisposed>(this, _ =>
//        {
//            Svc.Logger.Warning("All Moodle Actor Data as Moodles has been disposed.");
//            _actorData.Clear();
//        });
//        _moodles.OnManagerModified.Subscribe(OnManagerModified);
//    }


//    // Maybe dont expose this directly, use methods instead.
//    public static IReadOnlyDictionary<nint, MoodleActorInfo> Actors => _actorData;

//    protected override void Dispose(bool disposing)
//    {
//        base.Dispose(disposing);
//        _moodles.OnManagerModified.Unsubscribe(OnManagerModified);
//    }

//    private void OnManagerModified(nint actorPtr)
//    {
//        // Grab the data info for it, if the string is empty, then remove the actor.
//        var dataInfo = _moodles.GetStatusManagerInfoByPtr.InvokeFunc(actorPtr);
//        if (dataInfo.Count is 0)
//        {
//            _actorData.Remove(actorPtr);
//            return;
//        }
//        else if (_actorData.TryGetValue(actorPtr, out var existing))
//        {
//            existing.StatusInfos = dataInfo;
//            existing.Ids = dataInfo.Select(s => s.GUID).ToHashSet();
//        }
//        else
//        {
//            _actorData[actorPtr] = new MoodleActorInfo
//            {
//                StatusInfos = dataInfo,
//                Ids = dataInfo.Select(s => s.GUID).ToHashSet()
//            };
//        }
//    }

//    /// <summary>
//    ///     Attempt to retrieve an entry for an actor pointer, assuming they are a moodles actor.
//    /// </summary>
//    public void AddPossibleActor(nint actorPtr)
//    {
//        if (!IpcCallerMoodles.APIAvailable)
//            return;
//        // Attempt to get the data.
//        var dataInfo = _moodles.GetStatusManagerInfoByPtr.InvokeFunc(actorPtr);
//        if (dataInfo.Count is 0)
//            return;
//        // Create it.
//        _actorData[actorPtr] = new MoodleActorInfo()
//        {
//            StatusInfos = dataInfo,
//            Ids = dataInfo.Select(s => s.GUID).ToHashSet(),
//        };
//    }

//    // Maybe dont expose this directly, use methods instead.

//    /// <summary>
//    ///     Retrieves the actor info for a given pointer. Must be called on the framework thread.
//    /// </summary>
//    public MoodleActorInfo GetActorInfo(nint actorPtr)
//        => _actorData.TryGetValue(actorPtr, out var existing) ? existing : MoodleActorInfo.Empty;

//}