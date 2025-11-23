using CkCommons;
using CkCommons.Gui;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using System.Collections.Immutable;

namespace Sundouleia.Gui;

public class DebugPersonalDataUI : WindowMediatorSubscriberBase
{
    private readonly FolderConfig _config;
    private readonly SundesmoManager _pairs;
    public DebugPersonalDataUI(ILogger<DebugPersonalDataUI> logger, SundouleiaMediator mediator,
        FolderConfig config, SundesmoManager pairs) : base(logger, mediator, "Own Data Debug")
    {
        _config = config;
        _pairs = pairs;
        // Ensure the list updates properly.
        Mediator.Subscribe<FolderUpdateSundesmos>(this, _ => UpdateList());
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
            .OrderByDescending(u => u.IsRendered)
            .ThenByDescending(u => u.IsOnline)
            .ThenBy(pair => !pair.PlayerName.IsNullOrEmpty()
                ? (_config.Current.NickOverPlayerName ? pair.GetNickAliasOrUid() : pair.PlayerName)
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
            if (FancySearchBar.Draw("##PairDebugSearch", ImGui.GetContentRegionAvail().X, ref _searchValue, "Search for Pair..", 40))
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
        DrawOwnGlobals();
    }

    private void DrawPairData(Sundesmo sundesmo, float width)
    {
        var nick = sundesmo.GetNickAliasOrUid();
        using var node = ImRaii.TreeNode($"{nick}'s Pair Info##{sundesmo.UserData.UID}_info");
        if (!node) return;

        DrawSundesmoInfo(sundesmo);
        sundesmo.DrawRenderDebug(); 
        
        DrawGlobalPermissions(sundesmo);
        DrawPairPerms(sundesmo);
        CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());
    }

    private void DrawIconBoolColumn(bool value)
    {
        ImGui.TableNextColumn();
        CkGui.IconText(value ? FAI.Check : FAI.Times, value ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
    }


    private void DrawPermissionRowBool(string name, bool value)
    {
        ImGuiUtil.DrawTableColumn(name);
        DrawIconBoolColumn(value);
    }

    private void DrawPermissionRowString(string name, string value)
    {
        ImGuiUtil.DrawTableColumn(name);
        ImGui.TableNextColumn();
        ImGui.Text(value);
    }

    private void DrawUserPermRowBool(string name, bool valueOwn, bool valueOther)
    {
        ImGuiUtil.DrawTableColumn(name);
        DrawIconBoolColumn(valueOwn);
        DrawIconBoolColumn(valueOther);
    }

    private void DrawOwnGlobals()
    {
        using var nodeMain = ImRaii.TreeNode($"Globals##player-Globals");
        if (!nodeMain) return;

        var perms = MainHub.GlobalPerms;
        using (var table = ImRaii.Table("##debug-global-player", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            if (!table) return;
            ImGui.TableSetupColumn("Permission");
            ImGui.TableSetupColumn("Value");
            ImGui.TableHeadersRow();

            DrawPermissionRowBool("Default Allow Animations", perms.DefaultAllowAnimations);
            DrawPermissionRowBool("Default Allow Sounds", perms.DefaultAllowSounds);
            DrawPermissionRowBool("Default Allow Vfx", perms.DefaultAllowVfx);
        }
        ImGui.Separator();
    }


    private void DrawGlobalPermissions(Sundesmo s)
    {
        using var nodeMain = ImRaii.TreeNode($"Globals##{s.UserData.UID}-Globals");
        if (!nodeMain) return;

        var perms = s.PairGlobals;
        using (var table = ImRaii.Table("##debug-global" + s.UserData.UID, 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            if (!table) return;
            ImGui.TableSetupColumn("Permission");
            ImGui.TableSetupColumn("Value");
            ImGui.TableHeadersRow();

            DrawPermissionRowBool("Default Allow Animations", perms.DefaultAllowAnimations);
            DrawPermissionRowBool("Default Allow Sounds", perms.DefaultAllowSounds);
            DrawPermissionRowBool("Default Allow Vfx", perms.DefaultAllowVfx);
        }
    }

    private void DrawPairPerms(Sundesmo s)
    {
        using var nodeMain = ImRaii.TreeNode($"PairPerms##{s.UserData.UID}-pairperms");
        if (!nodeMain) return;

        using (var table = ImRaii.Table("##debug-pair" + s.UserData.UID, 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            if (!table) return;
            ImGui.TableSetupColumn("Permission");
            ImGui.TableSetupColumn("Own Setting");
            ImGui.TableSetupColumn($"{s.GetNickAliasOrUid()}'s Setting");
            ImGui.TableHeadersRow();

            DrawUserPermRowBool("Is Paused", s.OwnPerms.PauseVisuals, s.PairPerms.PauseVisuals);
            DrawUserPermRowBool("Allows Animations", s.OwnPerms.AllowAnimations, s.PairPerms.AllowAnimations);
            DrawUserPermRowBool("Allows Sounds", s.OwnPerms.AllowSounds, s.PairPerms.AllowSounds);
            DrawUserPermRowBool("Allows Vfx", s.OwnPerms.AllowVfx, s.PairPerms.AllowVfx);
        }
    }

    private void DrawSundesmoInfo(Sundesmo s)
    {
        using (var t = ImRaii.Table("##debug-info" + s.UserData.UID, 9, ImGuiTableFlags.SizingFixedFit))
        {
            if (!t) return;

            ImGui.TableSetupColumn("Nickname");
            ImGui.TableSetupColumn("Alias");
            ImGui.TableSetupColumn("UID");
            ImGui.TableSetupColumn("IsTemp");
            ImGui.TableSetupColumn("Paused");
            ImGui.TableSetupColumn("Reloading");
            ImGui.TableSetupColumn("Online");
            ImGui.TableSetupColumn("Visible");
            ImGui.TableSetupColumn("Chara Ident");
            ImGui.TableHeadersRow();

            ImGui.TableNextColumn();
            CkGui.ColorText(s.GetNickname() ?? "(None Set)", ImGuiColors.DalamudViolet);
            ImGui.TableNextColumn();
            if (s.UserData.Alias is not null)
                CkGui.ColorText(s.UserData.Alias, ImGuiColors.DalamudViolet);
            ImGui.TableNextColumn();
            CkGui.ColorText(s.UserData.UID, ImGuiColors.DalamudViolet);

            DrawIconBoolColumn(s.IsTemporary);
            DrawIconBoolColumn(s.IsPaused);
            DrawIconBoolColumn(s.IsReloading);
            DrawIconBoolColumn(s.IsOnline);
            DrawIconBoolColumn(s.IsRendered);
            ImGuiUtil.DrawFrameColumn(s.Ident);
        }
    }
}
