using CkCommons.DrawSystem;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;

namespace Sundouleia.DrawSystem;
public static class SorterExtensions
{
    // Used here as Sundesmo is shared commonly across multiple draw systems.
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByRendered = new Rendered();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByOnline = new Online();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByFavorite = new Favorite();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByPairName = new PairName();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByTemporary = new Temporary();
    public static readonly ISortMethod<DynamicLeaf<Sundesmo>> ByDateAdded = new DateAdded();

    public static readonly IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> AllGroupSteps
        = [ByRendered, ByOnline, ByFavorite, ByPairName, ByTemporary, ByDateAdded];

    // Converters
    public static ISortMethod<DynamicLeaf<Sundesmo>> ToSortMethod(this FolderSortFilter filter)
        => filter switch
        {
            FolderSortFilter.Rendered => ByRendered,
            FolderSortFilter.Online => ByOnline,
            FolderSortFilter.Favorite => ByFavorite,
            FolderSortFilter.Alphabetical => ByPairName,
            FolderSortFilter.Temporary => ByTemporary,
            FolderSortFilter.DateAdded => ByDateAdded,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };

    public static FolderSortFilter ToFolderSortFilter(this ISortMethod<DynamicLeaf<Sundesmo>> sortMethod)
        => sortMethod switch
        {
            Rendered => FolderSortFilter.Rendered,
            Online => FolderSortFilter.Online,
            Favorite => FolderSortFilter.Favorite,
            PairName => FolderSortFilter.Alphabetical,
            Temporary => FolderSortFilter.Temporary,
            DateAdded => FolderSortFilter.DateAdded,
            _ => throw new ArgumentOutOfRangeException(nameof(sortMethod), sortMethod, null)
        };

    /// <summary>
    ///     Preset for the AllFolder, to sort by name -> visible -> online -> favorite.
    /// </summary>
    public static readonly IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> AllFolderSorter
        = [ ByRendered, ByOnline, ByFavorite, ByPairName ];

    // Sort Helpers

    public struct Rendered : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Rendered";
        public FAI Icon => FAI.Eye; // Maybe change.
        public string Tooltip => "Sort by rendered status.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.IsRendered ? 0 : 1;
    }

    public struct Online : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Online";
        public FAI Icon => FAI.Wifi; // Maybe change.
        public string Tooltip => "Sort by online status.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.IsOnline ? 0 : 1;
    }

    public struct Favorite : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Favorite";
        public FAI Icon => FAI.Star; // Maybe change.
        public string Tooltip => "Sort by favorite status.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.IsFavorite ? 0 : 1;
    }

    public struct Temporary : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Temporary";
        public FAI Icon => FAI.Clock; // Maybe change.
        public string Tooltip => "Sort temporary pairs.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.IsTemporary ? 0 : 1;
    }

    public struct DateAdded : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Date Added";
        public FAI Icon => FAI.Calendar; // Maybe change.
        public string Tooltip => "Sort by date added.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.UserPair.CreatedAt;
    }

    public struct PairName : ISortMethod<DynamicLeaf<Sundesmo>>
    {
        public string Name => "Name";
        public FAI Icon => FAI.SortAlphaDown; // Maybe change.
        public string Tooltip => "Sort by name.";
        public Func<DynamicLeaf<Sundesmo>, IComparable?> KeySelector => l => l.Data.AlphabeticalSortKey();
    }

    public struct RadarName : ISortMethod<DynamicLeaf<RadarUser>>
    {
        public string Name => "Name";
        public FAI Icon => FAI.SortAlphaDown; // Maybe change.
        public string Tooltip => "Sort by name.";
        public Func<DynamicLeaf<RadarUser>, IComparable?> KeySelector => l => l.Data.DisplayName;
    }

    public struct ByRequestTime : ISortMethod<DynamicLeaf<RequestEntry>>
    {
        public string Name => "Request Time";
        public FAI Icon => FAI.Stopwatch;
        public string Tooltip => "Sort by request time.";
        public Func<DynamicLeaf<RequestEntry>, IComparable?> KeySelector => l => l.Data.ExpireTime;
    }
}

