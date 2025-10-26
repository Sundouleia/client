using CkCommons.Gui;
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

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var icon = Items[globalIdx];
        return CkGui.IconButton(icon, disabled: selected, inPopup: true);
    }

    /// <summary> Simple draw invoke. </summary>
    public bool Draw(string label, FAI preview, float comboWidth, int iconsPerRow, CFlags flags = CFlags.None)
    {
        ItemsPerRow = iconsPerRow;
        return base.Draw($"##{label}", preview.ToIconString(), comboWidth, flags);
    }

    public bool DrawPopup(string label, int iconsPerRow, Vector2 drawPos, uint? searchBg = null)
    {
        ItemsPerRow = iconsPerRow;
        return DrawPopup($"##{label}", drawPos, searchBg);
    }
}
