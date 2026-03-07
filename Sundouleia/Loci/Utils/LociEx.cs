using CkCommons.Gui;
using CkCommons.RichText;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using NAudio.SoundFont;
using Sundouleia.Loci;
using Sundouleia.Loci.Data;
using Sundouleia.Pairs;
using SundouleiaAPI.Data.Permissions;

namespace Sundouleia;
public static class LociEx
{
    /// <summary>
    ///     Have a blank, and multi selected compressed text output for a printed multi-selection.
    /// </summary>
    public static string PrintRange(this IEnumerable<string> s, out string FullList, string noneStr = "Any")
    {
        FullList = null!;
        var list = s.ToArray();
        if (list.Length is 0)
            return noneStr;
        if (list.Length is 1)
            return list[0].ToString();
        FullList = string.Join("\n", list.Select(x => x.ToString()));
        return $"{list.Length} selected";
    }

    public static void AttachTooltip(this LociStatus item, LociManager manager)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.RectOnly))
            return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(350f, 0f), new Vector2(350f, float.MaxValue));
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One * 8f)
            .Push(ImGuiStyleVar.WindowRounding, 4f)
            .Push(ImGuiStyleVar.PopupBorderSize, 1f);
        using var c = ImRaii.PushColor(ImGuiCol.Border, SundCol.Gold.Uint());
        using var tt = ImRaii.Tooltip();

        // push the title, converting all color tags into the actual label.
        CkRichText.Text(item.Title, cloneId: 100);
        if (!item.Description.IsNullOrWhitespace())
        {
            ImGui.Separator();
            CkRichText.Text(350f, item.Description);
        }

        CkGui.ColorText("Duration:", ImGuiColors.ParsedGold);
        var length = TimeSpan.FromTicks(item.NoExpire ? -1 : item.TotalMilliseconds);
        ImGui.SameLine();
        ImGui.Text($"{length.Days}d {length.Hours}h {length.Minutes}m {length.Seconds}");

        CkGui.ColorText("Category:", ImGuiColors.ParsedGold);
        ImGui.SameLine();
        ImGui.Text(item.Type.ToString());

        if (item.ChainedGUID != Guid.Empty)
        {
            if (item.ChainedType is ChainType.Status)
            {
                CkGui.ColorText("Chained Status:", ImGuiColors.ParsedGold);
                ImGui.SameLine();
                var status = manager.SavedStatuses.FirstOrDefault(x => x.GUID == item.ChainedGUID)?.Title ?? "Unknown";
                CkRichText.Text(status, 100);
            }
            else
            {
                CkGui.ColorText("Chained Preset:", ImGuiColors.ParsedGold);
                ImGui.SameLine();
                var preset = manager.SavedPresets.FirstOrDefault(x => x.GUID == item.ChainedGUID)?.Title ?? "Unknown";
                CkRichText.Text(preset, 100);
            }
        }
    }

    public static void AttachTooltip(this LociStatusInfo item, IEnumerable<LociStatusInfo> statuses, IEnumerable<LociPresetInfo> presets)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;

        ImGui.SetNextWindowSizeConstraints(new Vector2(350f, 0f), new Vector2(350f, float.MaxValue));
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One * 8f)
            .Push(ImGuiStyleVar.WindowRounding, 4f)
            .Push(ImGuiStyleVar.PopupBorderSize, 1f);
        using var c = ImRaii.PushColor(ImGuiCol.Border, SundCol.Gold.Uint());
        using var tt = ImRaii.Tooltip();

        // push the title, converting all color tags into the actual label.
        CkRichText.Text(item.Title, cloneId: 100);
        if (!item.Description.IsNullOrWhitespace())
        {
            ImGui.Separator();
            CkRichText.Text(350f, item.Description);
        }

        CkGui.ColorText("Duration:", ImGuiColors.ParsedGold);
        var length = TimeSpan.FromTicks(item.ExpireTicks);
        ImGui.SameLine();
        ImGui.Text($"{length.Days}d {length.Hours}h {length.Minutes}m {length.Seconds}");

        CkGui.ColorText("Category:", ImGuiColors.ParsedGold);
        ImGui.SameLine();
        ImGui.Text(item.Type.ToString());

        if (item.ChainedGUID != Guid.Empty)
        {
            if (item.ChainType is ChainType.Status)
            {
                CkGui.ColorText("Chained Status:", ImGuiColors.ParsedGold);
                ImGui.SameLine();
                var status = statuses.FirstOrDefault(x => x.GUID == item.ChainedGUID).Title ?? "Unknown";
                CkRichText.Text(status, 100);
            }
            else
            {
                CkGui.ColorText("Chained Preset:", ImGuiColors.ParsedGold);
                ImGui.SameLine();
                var preset = presets.FirstOrDefault(x => x.GUID == item.ChainedGUID).Title ?? "Unknown";
                CkRichText.Text(preset, 100);
            }
        }
    }

    /// <summary>
    ///     API Tuple format of if we can apply statuses or not.
    /// </summary>
    public static bool CanApply(PairPerms perms, IEnumerable<LociStatusInfo> statuses)
    {
        foreach (var status in statuses)
        {
            if (status.Type is StatusType.Positive && !perms.LociAccess.HasAny(LociAccess.Positive))
            {
                Svc.Toasts.ShowError("You do not have permission to apply Positive Statuses.");
                return false;
            }
            else if (status.Type is StatusType.Negative && !perms.LociAccess.HasAny(LociAccess.Negative))
            {
                Svc.Toasts.ShowError("You do not have permission to apply Negative Statuses.");
                return false;

            }
            else if (status.Type is StatusType.Special && !perms.LociAccess.HasAny(LociAccess.Special))
            {
                Svc.Toasts.ShowError("You do not have permission to apply Special Statuses.");
                return false;
            }
            else if (status.ExpireTicks > 0)
            {
                var totalTime = TimeSpan.FromMilliseconds(status.ExpireTicks - DateTimeOffset.Now.ToUnixTimeMilliseconds());
                if (totalTime > perms.MaxLociTime)
                {
                    Svc.Toasts.ShowError("You do not have permission to apply Statuses for that long.");
                    return false;
                }
            }
        }
        // return true if reached here.
        return true;
    }

    /// <summary>
    ///     Checks whether the given statuses can be applied according to the user's permissions.
    /// </summary>
    public static bool CanApply(PairPerms perms, IEnumerable<LociStatus> statuses)
    {
        foreach (var status in statuses)
        {
            if (status.Type is StatusType.Positive && !perms.LociAccess.HasAny(LociAccess.Positive))
            {
                Svc.Toasts.ShowError("You do not have permission to apply Positive Statuses.");
                return false;
            }
            else if (status.Type is StatusType.Negative && !perms.LociAccess.HasAny(LociAccess.Negative))
            {
                Svc.Toasts.ShowError("You do not have permission to apply Negative Statuses.");
                return false;
            }
            else if (status.Type is StatusType.Special && !perms.LociAccess.HasAny(LociAccess.Special))
            {
                Svc.Toasts.ShowError("You do not have permission to apply Special Statuses.");
                return false;
            }
            else if (status.ExpiresAt <= 0 && !perms.LociAccess.HasAny(LociAccess.Permanent))
            {
                // Treat ExpiresAt <= 0 as permanent status
                Svc.Toasts.ShowError("You do not have permission to apply Permanent Statuses.");
                return false;
            }
            else if (status.ExpiresAt > 0)
            {
                var totalTime = TimeSpan.FromMilliseconds(status.ExpiresAt - DateTimeOffset.Now.ToUnixTimeMilliseconds());
                if (totalTime > perms.MaxLociTime)
                {
                    Svc.Toasts.ShowError("You do not have permission to apply Statuses for that long.");
                    return false;
                }
            }
        }
        // All statuses passed permission checks
        return true;
    }


}
