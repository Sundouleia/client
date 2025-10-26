using Sundouleia.Pairs;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.Handlers;

public record SortInstruction(FolderSortFilter Filter, bool Ascending = false);
public sealed class FolderSortBuilder
{
    private readonly IEnumerable<Sundesmo> _source;
    private readonly List<SortInstruction> _instructions = new();

    public FolderSortBuilder(IEnumerable<Sundesmo> source)
    {
        _source = source;
    }

    public FolderSortBuilder Add(FolderSortFilter filter, bool ascending = false)
    {
        _instructions.Add(new SortInstruction(filter, ascending));
        return this;
    }

    public List<Sundesmo> Build()
    {
        if (_instructions.Count == 0)
            _instructions.Add(new SortInstruction(FolderSortFilter.Alphabetical));

        IOrderedEnumerable<Sundesmo>? ordered = null;

        foreach (var (filter, ascending) in _instructions)
        {
            var keySelector = GetKeySelector(filter);
            if (ordered == null)
                ordered = ascending
                    ? _source.OrderBy(keySelector)
                    : _source.OrderByDescending(keySelector);
            else
                ordered = ascending
                    ? ordered.ThenBy(keySelector)
                    : ordered.ThenByDescending(keySelector);
        }

        return ordered?.ToList() ?? _source.ToList();
    }

    // Provides mapping between FolderSortFilter and actual property accessors.
    private Func<Sundesmo, IComparable?> GetKeySelector(FolderSortFilter filter)
    {
        return filter switch
        {
            FolderSortFilter.Rendered => u => u.IsRendered,
            FolderSortFilter.Online => u => u.IsOnline,
            FolderSortFilter.Temporary => u => u.IsTemporary,
            FolderSortFilter.Favorite => u => u.IsFavorite,
            _ => u => u.AlphabeticalSortKey(),
        };
    }
}