using CkCommons.Helpers;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Extensions;
using OtterGui.Text;
using Sundouleia.PlayerClient;
using Sundouleia.Services;

namespace Sundouleia.CustomCombos;

public sealed class SavedStatusesCombo : CkFilterComboCache<LociStatusInfo>
{   
    private Guid _current;
    private static Vector2 _iconSize => LociIcon.Size;

    public SavedStatusesCombo(ILogger log, Func<IReadOnlyList<LociStatusInfo>> generator) : base(generator, log)
    {
        _current = Guid.Empty;
        SearchByParts = true;
    }

    public string HintText { get; set; } = "Status to Chain... (Optional)";
    protected override string ToString(LociStatusInfo status)
        => status.Title.StripColorTags();

    protected override int UpdateCurrentSelected(int currentSelected)
    {
        if (Current.GUID == _current)
            return currentSelected;

        CurrentSelectionIdx = Items.IndexOf(i => i.GUID == _current);
        Current = CurrentSelectionIdx >= 0 ? Items[CurrentSelectionIdx] : default;
        return CurrentSelectionIdx;
    }

    /// <summary> An override to the normal draw method that forces the current item to be the item passed in. </summary>
    /// <returns> True if a new item was selected, false otherwise. </returns>
    public bool Draw(string label, Guid curr, float width, float scaler = 1.0f)
        => Draw(label, curr, width, scaler, CFlags.None);

    public bool Draw(string label, Guid curr, float width, float scaler, CFlags flags)
    {
        InnerWidth = width * scaler;
        _current = curr;
        var preview = Items.FirstOrDefault(i => i.GUID == curr) is { } cur ? cur.Title.StripColorTags() : HintText;
        return Draw(label, preview, string.Empty, width, _iconSize.Y, flags);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var size = new Vector2(GetFilterWidth(), _iconSize.Y);
        var titleSpace = size.X - _iconSize.X;
        var myStatus = Items[globalIdx];
        using var dis = ImRaii.Disabled(myStatus.GUID == _current);

        // Push the font first so the height is correct.
        using var _ = Fonts.Default150Percent.Push();

        var ret = ImGui.Selectable($"##{myStatus.Title}", selected, ImGuiSelectableFlags.None, size);

        ImGui.SameLine(titleSpace);
        LociIcon.Draw(myStatus.IconID, myStatus.Stacks, _iconSize);
        SundouleiaEx.AttachTooltip(myStatus, LociData.Cache);

        ImGui.SameLine(ImUtf8.ItemInnerSpacing.X);
        var adjust = (size.Y - ImUtf8.TextHeight) * 0.5f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + adjust);
        CkRichText.Text(titleSpace, myStatus.Title);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - adjust);
        return ret;
    }
}
