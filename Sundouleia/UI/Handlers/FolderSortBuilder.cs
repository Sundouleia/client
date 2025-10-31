using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.Handlers;
public sealed class FolderSortBuilder
{
    private readonly IEnumerable<DrawEntitySundesmo> _source;
    private readonly List<FolderSortFilter> _instructions = new();

    public FolderSortBuilder(IEnumerable<DrawEntitySundesmo> source)
    {
        _source = source;
    }

    public FolderSortBuilder Add(FolderSortFilter filter)
    {
        _instructions.Add(filter);
        return this;
    }

    public List<DrawEntitySundesmo> Build()
    {
        if (_instructions.Count == 0)
            _instructions.Add(FolderSortFilter.Alphabetical);

        IOrderedEnumerable<DrawEntitySundesmo>? ordered = null;

        foreach (var filter in _instructions)
        {
            var keySelector = GetKeySelector(filter);
            if (ordered == null)
                ordered = filter is FolderSortFilter.Alphabetical
                    ? _source.OrderBy(keySelector)
                    : _source.OrderByDescending(keySelector);
            else
                ordered = filter is FolderSortFilter.Alphabetical
                    ? ordered.ThenBy(keySelector)
                    : ordered.ThenByDescending(keySelector);
        }

        return ordered?.ToList() ?? _source.ToList();
    }

    // Provides mapping between FolderSortFilter and actual property accessors.
    private Func<DrawEntitySundesmo, IComparable?> GetKeySelector(FolderSortFilter filter)
    {
        return filter switch
        {
            FolderSortFilter.Rendered => u => u.Item.IsRendered,
            FolderSortFilter.Online => u => u.Item.IsOnline,
            FolderSortFilter.Temporary => u => u.Item.IsTemporary,
            FolderSortFilter.Favorite => u => u.Item.IsFavorite,
            _ => u => u.Item.AlphabeticalSortKey(),
        };
    }
}

public sealed class FolderSortBuilder<TModel>
{
    private readonly IEnumerable<TModel> _source;
    private readonly List<FolderSortFilter> _instructions = new();
    private readonly Func<FolderSortFilter, Func<TModel, IComparable?>> _keySelectorProvider;

    public FolderSortBuilder(IEnumerable<TModel> source, Func<FolderSortFilter, Func<TModel, IComparable?>> keySelectorProvider)
    {
        _source = source;
        _keySelectorProvider = keySelectorProvider;
    }

    public FolderSortBuilder<TModel> Add(FolderSortFilter filter)
    {
        _instructions.Add(filter);
        return this;
    }

    public List<TModel> Build()
    {
        if (_instructions.Count == 0)
            _instructions.Add(FolderSortFilter.Alphabetical);

        IOrderedEnumerable<TModel>? ordered = null;

        foreach (var filter in _instructions)
        {
            var keySelector = _keySelectorProvider(filter);

            if (ordered == null)
                ordered = filter == FolderSortFilter.Alphabetical
                    ? _source.OrderBy(keySelector)
                    : _source.OrderByDescending(keySelector);
            else
                ordered = filter == FolderSortFilter.Alphabetical
                    ? ordered.ThenBy(keySelector)
                    : ordered.ThenByDescending(keySelector);
        }

        return ordered?.ToList() ?? _source.ToList();
    }
}