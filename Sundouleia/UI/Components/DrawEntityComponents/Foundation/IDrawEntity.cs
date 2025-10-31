namespace Sundouleia.Gui.Components;

public interface IDrawEntity<TModel> : IDrawEntity
{
    /// <summary>
    ///     The underlying model for this draw entity.
    /// </summary>
    TModel Item { get; }
}

public interface IDrawEntity
{
    /// <summary>
    ///     Unique Identification, assigned on initialization, usually folder + UID
    /// </summary>
    string DistinctId { get; }

    /// <summary>
    ///     Every entry should have a UID in some manner 
    ///     (request target UID, radar UID, sundesmo UID)
    /// </summary>
    string UID { get; }

    /// <summary>
    ///     Name shown on UI Displays, can become anonymous if needed.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    ///     Main Draw Call for the Entity.
    /// </summary>
    /// <returns>
    ///     True if selected, false otherwise.
    /// </returns>
    bool Draw(bool selected);
}

