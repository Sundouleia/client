using CkCommons.Helpers;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;

namespace Sundouleia.CustomCombos;

public sealed class OwnPresetCombo : LociComboBase<LociPresetStruct>
{
    private int _maxPresetCount => LociData.Cache.PresetList.Max(x => x.Statuses.Count);
    private float _iconWithPadding => IconSize.X + ImUtf8.ItemInnerSpacing.X;
    public OwnPresetCombo(ILogger log, MainHub hub, Sundesmo sundesmo, float scale)
        : base(log, hub, sundesmo, scale, () => [.. LociData.Cache.PresetList.OrderBy(x => x.Title.StripColorTags())])
    { }

    protected override bool DisableCondition()
        => Current.GUID == Guid.Empty || !_sundesmo.PairPerms.LociAccess.HasAny(LociAccess.AllowOther);

    protected override string ToString(LociPresetStruct LociPreset)
        => LociPreset.Title.StripColorTags();

    public bool DrawApplyPresets(string id, float width, string buttonTT)
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

        // Push the font first so the height is correct.
        using var _ = Fonts.Default150Percent.Push();

        var ret = ImGui.Selectable($"##{lociPreset.Title}", selected, ImGuiSelectableFlags.None, size);

        if (lociPreset.Statuses.Count > 0)
        {
            ImGui.SameLine(titleSpace);
            for (int i = 0; i < lociPreset.Statuses.Count; i++)
            {
                var status = lociPreset.Statuses[i];
                if (LociData.Cache.Statuses.TryGetValue(status, out var info))
                {
                    ImGui.SameLine(0, _iconWithPadding);
                    continue;
                }

                LociIcon.Draw(info.IconID, info.Stacks, IconSize);
                SundouleiaEx.AttachTooltip(info, LociData.Cache);

                if (i < lociPreset.Statuses.Count)
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

    // We can technically optimize this to send the preset tuples themselves, but whatever.
    protected override bool CanDoAction(LociPresetStruct item)
    {
        var ids = item.Statuses.ToHashSet();
        var toCheck = LociData.Cache.StatusList.Where(s => ids.Contains(s.GUID));
        return SundouleiaEx.CanApply(_sundesmo.PairPerms, toCheck);
    }

    // We can technically optimize this to send the preset tuples themselves, but whatever.
    protected override void OnApplyButton(LociPresetStruct item)
    {
        if (!CanDoAction(item))
            return;

        UiService.SetUITask(async () =>
        {
            var ids = item.Statuses.ToHashSet();
            var toSend = LociData.Cache.StatusList.Where(s => ids.Contains(s.GUID));
            var res = await _hub.UserApplyLociStatusTuples(new(_sundesmo.UserData, toSend));
            if (res.ErrorCode is not SundouleiaApiEc.Success)
                Log.LogWarning($"Failed to apply loci preset {item.Title} on {_sundesmo.GetNickAliasOrUid()}: [{res.ErrorCode}]");
        });
    }
}
