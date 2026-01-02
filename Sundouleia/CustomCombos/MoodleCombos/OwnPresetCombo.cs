using CkCommons.Helpers;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;

namespace Sundouleia.CustomCombos;

public sealed class OwnPresetCombo : MoodleComboBase<MoodlePresetInfo>
{
    private int _maxPresetCount => ClientMoodles.Data.PresetList.Max(x => x.Statuses.Count);
    private float _iconWithPadding => IconSize.X + ImGui.GetStyle().ItemInnerSpacing.X;
    public OwnPresetCombo(ILogger log, MainHub hub, Sundesmo sundesmo, float scale)
        : base(log, hub, sundesmo, scale, () => [ .. ClientMoodles.Data.PresetList.OrderBy(x => x.Title) ])
    { }

    protected override bool DisableCondition()
        => Current.GUID == Guid.Empty || !_sundesmo.PairPerms.MoodleAccess.HasAny(MoodleAccess.AllowOther);

    protected override string ToString(MoodlePresetInfo obj)
        => obj.Title.StripColorTags();

    public bool DrawApplyPresets(string id, float width, string buttonTT)
    {
        InnerWidth = width + _iconWithPadding * _maxPresetCount;
        var prevLabel = Current.GUID == Guid.Empty ? "Select Presets.." : Current.Title.StripColorTags();
        return DrawComboButton(id, prevLabel, width, true, buttonTT);
    }

    protected override bool DrawSelectable(int globalIdx, bool selected)
    {
        var moodlePreset = Items[globalIdx];
        var size = new Vector2(GetFilterWidth(), IconSize.Y);
        var iconsSpace = (_iconWithPadding * moodlePreset.Statuses.Count);
        var titleSpace = size.X - iconsSpace;

        // Push the font first so the height is correct.
        using var _ = UiFontService.Default150Percent.Push();

        var ret = ImGui.Selectable($"##{moodlePreset.Title}", selected, ImGuiSelectableFlags.None, size);

        if (moodlePreset.Statuses.Count > 0)
        {
            ImGui.SameLine(titleSpace);
            for (int i = 0, iconsDrawn = 0; i < moodlePreset.Statuses.Count; i++)
            {
                var status = moodlePreset.Statuses[i];
                if (!ClientMoodles.Data.Statuses.TryGetValue(status, out var info))
                {
                    ImGui.SameLine(0, _iconWithPadding);
                    continue;
                }

                MoodleIcon.DrawMoodleIcon(info.IconID, info.Stacks, IconSize);
                info.AttachTooltip(ClientMoodles.Data.StatusList);

                if (++iconsDrawn < moodlePreset.Statuses.Count)
                    ImUtf8.SameLineInner();
            }
        }

        ImUtf8.SameLineInner();
        var adjust = (size.Y - ImUtf8.TextHeight) * 0.5f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + adjust);
        CkRichText.Text(titleSpace, moodlePreset.Title);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - adjust);
        return ret;
    }

    protected override bool CanDoAction(MoodlePresetInfo item)
    {
        var statuses = new List<MoodlesStatusInfo>(item.Statuses.Count);
        foreach (var guid in item.Statuses)
            if (ClientMoodles.Data.Statuses.TryGetValue(guid, out var s))
                statuses.Add(s);
        // Check application.
        return MoodlesEx.CanApplyMoodles(_sundesmo.PairPerms, statuses);
    }

    protected override void OnApplyButton(MoodlePresetInfo item)
    {
        if (!CanDoAction(item))
            return;

        UiService.SetUITask(async () =>
        {
            var statuses = new List<MoodlesStatusInfo>();
            foreach (var guid in item.Statuses)
                if (ClientMoodles.Data.Statuses.TryGetValue(guid, out var s))
                    statuses.Add(s);

            var res = await _hub.UserApplyMoodleTuples(new(_sundesmo.UserData, statuses));
            if (res.ErrorCode is SundouleiaApiEc.Success)
                Log.LogWarning($"Failed to apply moodle preset {item.Title} on {_sundesmo.GetNickAliasOrUid()}: [{res.ErrorCode}]");
        });
    }
}
