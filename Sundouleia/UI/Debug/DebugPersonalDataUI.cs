using CkCommons;
using CkCommons.Gui;
using CkCommons.Textures;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using SundouleiaAPI.Data.Permissions;
using OtterGui;
using OtterGui.Extensions;
using System.Collections.Immutable;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui;

public class DebugPersonalDataUI : WindowMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly SundesmoManager _pairs;
    public DebugPersonalDataUI(ILogger<DebugPersonalDataUI> logger, SundouleiaMediator mediator,
        MainConfig config, SundesmoManager pairs) : base(logger, mediator, "Own Data Debug")
    {
        _config = config;
        _pairs = pairs;
        // Ensure the list updates properly.
        Mediator.Subscribe<RefreshUiMessage>(this, _ => UpdateList());

        IsOpen = true;
        this.SetBoundaries(new Vector2(625, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    // For inspecting Sundesmo data.
    protected ImmutableList<Sundesmo> _immutablePairs = ImmutableList<Sundesmo>.Empty;
    protected string _searchValue = string.Empty;

    public void UpdateList()
    {
        // Get direct pairs, then filter them.
        var filteredPairs = _pairs.DirectPairs
            .Where(p =>
            {
                if (_searchValue.IsNullOrEmpty())
                    return true;
                // Match for Alias, Uid, Nick, or PlayerName.
                return p.UserData.AliasOrUID.Contains(_searchValue, StringComparison.OrdinalIgnoreCase)
                    || (p.GetNickname()?.Contains(_searchValue, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (p.PlayerName?.Contains(_searchValue, StringComparison.OrdinalIgnoreCase) ?? false);
            });

        // Take the remaining filtered list, and sort it.
        _immutablePairs = filteredPairs
            .OrderByDescending(u => u.PlayerRendered)
            .ThenByDescending(u => u.IsOnline)
            .ThenBy(pair => !pair.PlayerName.IsNullOrEmpty()
                ? (_config.Current.PreferNicknamesOverNames ? pair.GetNickAliasOrUid() : pair.PlayerName)
                : pair.GetNickAliasOrUid(), StringComparer.OrdinalIgnoreCase)
            .ToImmutableList();
    }

    protected override void DrawInternal()
    {
        if (ImGui.CollapsingHeader("Own Data"))
            OwnData();

        ImGui.Separator();
        if (ImGui.CollapsingHeader("Pair Data"))
        {
            ImGui.Text($"Total Pairs: {_pairs.DirectPairs.Count}");
            ImGui.Text($"Visible Users: {_pairs.GetVisibleCount()}");
            ImGui.Text($"Visible Rendered: {_pairs.GetVisibleConnected().Count}");

            // The search.
            if (FancySearchBar.Draw("##PairDebugSearch", ImGui.GetContentRegionAvail().X, "Search for Pair..", ref _searchValue, 40))
                UpdateList();

            // Separator, then the results.
            ImGui.Separator();
            var width = ImGui.GetContentRegionAvail().X;
            foreach (var pair in _immutablePairs)
                DrawPairData(pair, width);
        }
    }

    private void OwnData()
    {
        DrawGlobalPermissions("Player", MainHub.ConnectionResponse?.GlobalPerms ?? new());
    }

    private void DrawPairData(Sundesmo pair, float width)
    {
        var nick = pair.GetNickAliasOrUid();
        using var node = ImRaii.TreeNode($"{nick}'s Pair Info");
        if (!node) return;

        DrawPairPerms(nick, pair);
        DrawGlobalPermissions(pair.UserData.UID + "'s Global Perms", pair.PairGlobals);
        ImGui.Separator();
    }


    private void DrawPermissionRowBool(string name, bool value)
    {
        ImGuiUtil.DrawTableColumn(name);
        ImGui.TableNextColumn();
        CkGui.IconText(value ? FAI.Check : FAI.Times, value ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        ImGui.TableNextRow();
    }

    private void DrawUserPermRowBool(string name, bool valueOwn, bool valueOther)
    {
        ImGuiUtil.DrawTableColumn(name);
        ImGui.TableNextColumn();
        CkGui.IconText(valueOwn ? FAI.Check : FAI.Times, valueOwn ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        ImGui.TableNextColumn();
        CkGui.IconText(valueOther ? FAI.Check : FAI.Times, valueOther ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        ImGui.TableNextRow();
    }

    private void DrawUserPermRowString(string name, string valueOwn, string valueOther)
    {
        ImGuiUtil.DrawTableColumn(name);
        ImGuiUtil.DrawTableColumn(valueOwn);
        ImGuiUtil.DrawTableColumn(valueOther);
        ImGui.TableNextRow();
    }

    private void DrawGlobalPermissions(string uid, GlobalPerms perms)
    {
        using var nodeMain = ImRaii.TreeNode(uid + " Global Perms");
        if (!nodeMain) return;

        try
        {
            using var table = ImRaii.Table("##debug-global" + uid, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
            if (!table) return;
            ImGui.TableSetupColumn("Permission");
            ImGui.TableSetupColumn("Value");
            ImGui.TableHeadersRow();

            DrawPermissionRowBool("Default Allow Animations", perms.DefaultAllowAnimations);
            DrawPermissionRowBool("Default Allow Sounds", perms.DefaultAllowSounds);
            DrawPermissionRowBool("Default Allow Vfx", perms.DefaultAllowVfx);
        }
        catch (Bagagwa e)
        {
            _logger.LogError($"Error while drawing global permissions for {uid}: {e.Message}");
        }
    }

    private void DrawPairPerms(string label, Sundesmo k)
    {
        using var nodeMain = ImRaii.TreeNode($"{label}'s Pair Permissions");
        if (!nodeMain) return;

        using var table = ImRaii.Table("##debug-pair" + k.UserData.UID, 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        ImGui.TableSetupColumn("Permission");
        ImGui.TableSetupColumn("Own Setting");
        ImGui.TableSetupColumn($"{label}'s Setting");
        ImGui.TableHeadersRow();

        DrawUserPermRowBool("Is Paused", k.OwnPerms.PauseVisuals, k.PairPerms.PauseVisuals);
        ImGui.TableNextRow();

        DrawUserPermRowBool("Allows Animations", k.OwnPerms.AllowAnimations, k.PairPerms.AllowAnimations);
        DrawUserPermRowBool("Allows Sounds", k.OwnPerms.AllowSounds, k.PairPerms.AllowSounds);
        DrawUserPermRowBool("Allows Vfx", k.OwnPerms.AllowVfx, k.PairPerms.AllowVfx);
        ImGui.TableNextRow();
    }
}
