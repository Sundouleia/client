using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using OtterGui.Text;
using System.Drawing;

namespace Sundouleia.CustomCombos;

/// <summary> 
///     A combo for searching up Font Awesome Icons by their search term and/or category.
/// </summary>
public sealed class FAIconCombo : CkFilterComboGalleryCache<FAI>
{
    public FAIconCombo(ILogger log)
        : base(FontAwesomeHelpers.GetIcons, new(ImUtf8.FrameHeight), log)
    {
        SearchByParts = true;
    }

    protected override string ToString(FAI icon)
        => Enum.GetName(icon)?.ToLowerInvariant() ?? "unknown";

    /// <summary> Simple draw invoke. </summary>
    public bool Draw(string label, string preview, int iconsPerRow, CFlags flags = CFlags.None)
    {
        ItemsPerRow = iconsPerRow;
        return Draw(label, preview, iconsPerRow, flags);
    }

    public bool DrawPopup(string label, int iconsPerRow, Vector2 drawPos, uint? searchBg = null)
    {
        ItemsPerRow = iconsPerRow;
        return DrawPopup($"##{label}", drawPos, searchBg);
    }
}
