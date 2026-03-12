using CkCommons.Helpers;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;

namespace Sundouleia.CustomCombos;

public sealed class SundesmoPresetCombo : LociComboBase<LociPresetStruct>
{
    private int _maxPresetCount => _sundesmo.SharedData.PresetList.Max(x => x.Statuses.Count);
    private float _iconWithPadding => IconSize.X + ImGui.GetStyle().ItemInnerSpacing.X;

    public SundesmoPresetCombo(ILogger log, MainHub hub, Sundesmo sundesmo, float scale)
        : base(log, hub, sundesmo, scale, () => [.. sundesmo.SharedData.PresetList.OrderBy(x => x.Title)])
    { }

    protected override bool DisableCondition()
        => Current.GUID == Guid.Empty || !_sundesmo.PairPerms.LociAccess.HasAny(LociAccess.AllowOwn);
    protected override string ToString(LociPresetStruct obj)
        => obj.Title.StripColorTags();

    public bool DrawPresets(string id, float width, string buttonTT)
    {
        InnerWidth = width + _iconWithPadding * _maxPresetCount;
        var prevLabel = Current.GUID == Guid.Empty ? "Select Preset.." : Current.Title.StripColorTags();
        return DrawComboButton(id, prevLabel, width, true, buttonTT);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var lociPreset = Items[globalIdx];
        var size = new Vector2(GetFilterWidth(), IconSize.Y);
        var iconsSpace = (_iconWithPadding * lociPreset.Statuses.Count);
        var titleSpace = size.X - iconsSpace;
        var ret = ImGui.Selectable($"##{lociPreset.Title}", selected, ImGuiSelectableFlags.None, size);

        // Push the font first so the height is correct.
        using var _ = Fonts.Default150Percent.Push();

        if (lociPreset.Statuses.Count > 0)
        {
            ImGui.SameLine(titleSpace);
            for (int i = 0; i < lociPreset.Statuses.Count; i++)
            {
                var status = lociPreset.Statuses[i];
                if (!_sundesmo.SharedData.Statuses.TryGetValue(status, out var info))
                {
                    ImGui.SameLine(0, _iconWithPadding);
                    continue;
                }

                LociIcon.Draw(info.IconID, info.Stacks, IconSize);
                info.AttachTooltip(_sundesmo.SharedData);

                if (i + 1 < lociPreset.Statuses.Count)
                    ImUtf8.SameLineInner();
            }
        }

        ImGui.SameLine(ImUtf8.ItemInnerSpacing.X);
        var adjust = (size.Y - ImUtf8.TextHeight) * 0.5f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + adjust);
        CkRichText.Text(titleSpace, lociPreset.Title);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - adjust);
        return ret;
    }

    protected override bool CanDoAction(LociPresetStruct item)
    {
        var toCheck = new List<LociStatusStruct>(item.Statuses.Count);
        foreach (var guid in item.Statuses)
            if (_sundesmo.SharedData.Statuses.TryGetValue(guid, out var info))
                toCheck.Add(info);

        return SundouleiaEx.CanApply(_sundesmo.PairPerms, toCheck);
    }

    protected override void OnApplyButton(LociPresetStruct item)
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserApplyLociData(new(_sundesmo.UserData, item.Statuses, true));
            if (res.ErrorCode is not SundouleiaApiEc.Success)
                Log.LogWarning($"Failed to apply preset {item.Title} on {_sundesmo.GetNickAliasOrUid()}: [{res.ErrorCode}]");
        });
    }
}
