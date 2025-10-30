
using Sundouleia.Pairs;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     The contract requirement for a drawn folder containing Sundesmos. <para />
///     Primarily intended for the Whitelist tab but can be used elsewhere too.
/// </summary>
public interface ISundesmoFolder
{
    /// <summary>
    ///     Required to share memory between sundesmo folders.
    /// </summary>
    SharedFolderMemory SharedMemory { get; }

    /// <summary>
    ///     The Identifier label of this folder.
    /// </summary>
    string Label { get; }

    /// <summary>
    ///     The total sundesmos within this folder.
    /// </summary>
    int Total { get; }

    /// <summary>
    ///     The total sundesmos rendered within this folder.
    /// </summary>
    int Rendered { get; }

    /// <summary>
    ///     The total sundesmos online within this folder.
    /// </summary>
    int Online { get; }

    /// <summary>
    ///     The Entities to display.
    /// </summary>
    ImmutableList<DrawEntitySundesmo> DrawEntities { get; }

    /// <summary>
    ///     Primary Draw Function.
    /// </summary>
    void DrawContents();

    /// <summary>
    ///     Regenerate the 'DrawEntities' based on the provided filter."
    /// </summary>
    void RegenerateItems(string filter);

    /// <summary>
    ///     Update the filtered items based on the new provided filter.
    /// </summary>
    void UpdateItemsForFilter(string filter);

}

public interface ISundesmoEntity
{
    // Unique DrawTag
    string Identifier { get; }

    Sundesmo Sundesmo { get; }
}


/// <summary>
///     The contract requirement for any radar related draw folder.
/// </summary>
public interface IRadarFolder
{
    /// <summary>
    ///     The total radar users in the zone.
    /// </summary>
    int Total { get; }

    /// <summary>
    ///     How many of the radar users are rendered / allowing requests.
    /// </summary>
    int Rendered { get; }

    /// <summary>
    ///     How many of the radar users are lurkers (not rendered / invalid).
    /// </summary>
    int Lurkers { get; }

    /// <summary>
    ///     Public accessor immutable list for drawn radar user entities.
    /// </summary>
    IImmutableList<DrawEntityRadarUser> DrawEntities { get; }

    void Draw();
}
