using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Components;

/// <summary>
///     Base logic for drawn folders. Most if not all abstract draw logic is removed in order
///     to improve draw time performance, as calls invoked for draws that are abstract rack up
///     draw time quickly, especially when combined with interfaces. (learned this from CkRichText)
/// </summary>
public abstract class DrawFolderBase : IDrawFolder
{
    protected readonly MainConfig _config;
    protected readonly GroupsManager _manager;

    protected bool _hovered;

    // Required Stylization for all folders.
    protected uint _colorBG = uint.MinValue;
    protected uint _colorBorder = uint.MaxValue;

    protected FAI _icon = FAI.Folder;
    protected uint _iconColor = uint.MaxValue;

    protected readonly string _label;
    protected uint _labelColor = uint.MaxValue;

    // Tracks all Sundesmos involved with this folder.
    protected readonly IImmutableList<Sundesmo> _allSundesmos;

    protected DrawFolderBase(string label, IImmutableList<DrawEntitySundesmo> drawEntities, 
        IImmutableList<Sundesmo> allSundesmos, MainConfig config, GroupsManager manager)
    {
        _label = label;
        DrawEntities = drawEntities;
        _allSundesmos = allSundesmos;
        _config = config;
        _manager = manager;
    }

    // Interface satisfaction.
    public int Total => _allSundesmos.Count;
    public int Online => _allSundesmos.Count(s => s.IsOnline);
    public int Rendered => _allSundesmos.Count(s => s.IsRendered);
    public IImmutableList<DrawEntitySundesmo> DrawEntities { get; init; }

    /// <summary>
    ///     If this folder should be displayed when nobody in it is online.
    /// </summary>
    protected virtual bool RenderIfEmpty => true;

    public abstract void Draw();
}
