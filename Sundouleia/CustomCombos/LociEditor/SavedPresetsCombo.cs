using CkCommons.Helpers;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Extensions;
using OtterGui.Text;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using SundouleiaAPI.Data;

namespace Sundouleia.CustomCombos;

public sealed class SavedPresetsCombo : CkFilterComboCache<LociPresetStruct>
{  
    private Guid _current;
    private static Vector2 _iconSize => LociIcon.Size;

    public SavedPresetsCombo(ILogger log, Func<IReadOnlyList<LociPresetStruct>> generator)
        : base(generator, log)
    {
        _current = Guid.Empty;
        SearchByParts = true;
    }

    public string HintText { get; set; } = "Preset to Chain... (Optional)";
    protected override string ToString(LociPresetStruct status)
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
        var myPreset = Items[globalIdx];
        var iconsSpace = ((_iconSize.X + ImUtf8.ItemInnerSpacing.X) * myPreset.Statuses.Count);
        var titleSpace = size.X - iconsSpace;
        
        using var dis = ImRaii.Disabled(myPreset.GUID == _current);

        // Push the font first so the height is correct.
        using var _ = Fonts.Default150Percent.Push();

        var ret = ImGui.Selectable($"##{myPreset.Title}", selected, ImGuiSelectableFlags.None, size);

        if (LociData.Cache.Statuses.Count > 0)
        {
            ImGui.SameLine(titleSpace);
            for (int i = 0; i < myPreset.Statuses.Count; i++)
            {
                var status = myPreset.Statuses[i];
                // Maybe save on drawtime by doing internal referencing here or something.
                if (LociData.Cache.Statuses.TryGetValue(status, out var info))
                {
                    ImGui.SameLine(0, (_iconSize.X + ImUtf8.ItemInnerSpacing.X));
                    continue;
                }

                LociIcon.Draw(info.IconID, info.Stacks, _iconSize);
                SundouleiaEx.AttachTooltip(info, LociData.Cache);

                if (i < myPreset.Statuses.Count)
                    ImUtf8.SameLineInner();
            }
        }

        ImGui.SameLine(ImUtf8.ItemInnerSpacing.X);
        var adjust = (size.Y - ImUtf8.TextHeight) * 0.5f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + adjust);
        CkRichText.Text(titleSpace, myPreset.Title);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - adjust);
        return ret;
    }
}
