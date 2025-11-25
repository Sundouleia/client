using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Services;

namespace Sundouleia.CustomCombos;

/// <summary> 
///     A combo for searching up Font Awesome Icons by their search term and/or category.
/// </summary>
public sealed class FAIconCombo : CkFilterComboGalleryCache<FAI>
{
    private readonly ImRaii.Font _previewFAI = new();
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
    public bool Draw(string label, FAI preview, int iconsPerRow, CFlags flags = CFlags.None)
    {
        ItemsPerRow = iconsPerRow;
        _previewFAI.Push(Svc.PluginInterface.UiBuilder.FontIcon);
        var width = (flags & CFlags.NoArrowButton) == 0 ? (ImUtf8.FrameHeight * 2 + ImUtf8.ItemInnerSpacing.X) : ImUtf8.FrameHeight;
        return base.Draw($"##{label}", preview.ToIconString(), width, flags);
    }

    public bool DrawPopup(string label, int iconsPerRow, Vector2 drawPos, uint? searchBg = null)
    {
        ItemsPerRow = iconsPerRow;
        return DrawPopup($"##{label}", drawPos, searchBg);
    }

    protected override float GetInnerWidth()
    {
        // Hack to not FAI the hover tooltip or whatever.
        _previewFAI.Pop();
        return base.GetInnerWidth();
    }

    protected override void PostCombo(float previewWidth)
    {
        _previewFAI.Dispose();
    }
}
