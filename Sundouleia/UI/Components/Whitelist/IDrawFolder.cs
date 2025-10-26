
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     The contract requirement for a drawn folder containing Sundesmos. <para />
///     Primarily intended for the Whitelist tab but can be used elsewhere too.
/// </summary>
public interface IDrawFolder
{
    /// <summary>
    ///     The total sundesmos within this folder.
    /// </summary>
    int Total { get; }

    /// <summary>
    ///     How many of them are currently online. <para />
    ///     
    ///     NOTE: Doing this may break our logic unless we 
    ///     directly reference the list, if we want to prevent 
    ///     spamming recalculations on draw lists.
    /// </summary>
    int Online { get; }

    // Can add some kind of sort filter here if desired?

    /// <summary>
    ///     The sundesmo entries to draw when the folder is opened.
    /// </summary>
    IImmutableList<DrawEntitySundesmo> DrawEntities { get; }

    void Draw();
}
