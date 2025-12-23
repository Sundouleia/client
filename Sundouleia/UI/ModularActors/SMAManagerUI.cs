using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui.Text;
using Sundouleia.CustomCombos;
using Sundouleia.DrawSystem;
using Sundouleia.ModFiles;
using Sundouleia.ModularActor;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace Sundouleia.Gui;

public class SMAManagerUI : WindowMediatorSubscriberBase
{
    // Some config, probably.
    private readonly SMAFileHandler _handler;
    private readonly SMAFileManager _manager;
    private readonly TutorialService _guides;

    private OwnedModularActorData? _selectedSmad;
    private OwnedModularActorBase? _selectedSmab;
    private OwnedModularActorOutfit? _selectedSmao;
    private OwnedModularActorItem? _selectedSmai;

    public SMAManagerUI(ILogger<SMACreatorUI> logger, SundouleiaMediator mediator,
        SMAFileHandler handler, SMAFileManager manager, TutorialService guides) 
        : base(logger, mediator, "Modular Actor Manager###SundouleiaSMAManager")
    {
        _handler = handler;
        _manager = manager;
        _guides = guides;

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(500, 300), ImGui.GetIO().DisplaySize);
        // Add tutorial later.
    }
    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        CkGui.FontText("Modular Actor Files", UiFontService.UidFont);
        using var tabBar = ImRaii.TabBar("smaFileTabBar");
        DrawSMAData();
        DrawSMABase();
        DrawSMAOutfit();
        DrawSMAItem();
    }

    private void DrawSMAData()
    {
        using var tab = ImRaii.TabItem("SMAData");
        if (!tab) return;

        using var id = ImRaii.PushId("smad_info");
        DrawSmadInternal();
    }

    private void DrawSMABase()
    {
        using var tab = ImRaii.TabItem("SMABase");
        if (!tab) return;

        using var id = ImRaii.PushId("smab_info");
        DrawSmabInternal();
    }
    private void DrawSMAOutfit()
    {
        using var tab = ImRaii.TabItem("SMAOutfit");
        if (!tab) return;

        using var id = ImRaii.PushId("smao_info");
        DrawSmaoInternal();
    }

    private void DrawSMAItem()
    {
        using var tab = ImRaii.TabItem("SMAItem");
        if (!tab) return;

        using var id = ImRaii.PushId("smai_info");
        DrawSmaiInternal();
    }

    private void DrawSmadInternal()
    {
        using (ImRaii.ListBox("##owned_smad", new Vector2(125, ImGui.GetContentRegionAvail().Y)))
        {
            foreach (var actor in _manager.SMAD)
                if (ImGui.Selectable($"{actor.Base.Name}##smad_{actor.Description}", actor == _selectedSmad))
                    _selectedSmad = actor;
        }

        ImGui.SameLine();
        CkGui.FontText($"Selected: {(_selectedSmad is null ? "<none>" : _selectedSmad.Base.Name)}", UiFontService.Default150Percent);
        ImGui.Separator();
        ImGui.Text("Further Details here...");
    }

    private void DrawSmabInternal()
    {
        using (ImRaii.ListBox("##owned_smab", new Vector2(125, ImGui.GetContentRegionAvail().Y)))
        {
            foreach (var actor in _manager.Bases)
                if (ImGui.Selectable($"{actor.Name}##smab_{actor.Name}", actor == _selectedSmab))
                    _selectedSmab = actor;
        }

        ImGui.SameLine();
        CkGui.FontText($"Selected: {(_selectedSmab is null ? "<none>" : _selectedSmab.Name)}", UiFontService.Default150Percent);
        ImGui.Separator();
        ImGui.Text("Further Details here...");
    }

    private void DrawSmaoInternal()
    {
        using (ImRaii.ListBox("##owned_smao", new Vector2(125, ImGui.GetContentRegionAvail().Y)))
        {
            foreach (var actor in _manager.Outfits)
                if (ImGui.Selectable($"{actor.Name}##smao_{actor.Name}", actor == _selectedSmao))
                    _selectedSmao = actor;
        }

        ImGui.SameLine();
        CkGui.FontText($"Selected: {(_selectedSmao is null ? "<none>" : _selectedSmao.Name)}", UiFontService.Default150Percent);
        ImGui.Separator();
        ImGui.Text("Further Details here...");
    }

    private void DrawSmaiInternal()
    {
        using (ImRaii.ListBox("##owned_smai", new Vector2(125, ImGui.GetContentRegionAvail().Y)))
        {
            foreach (var actor in _manager.Items)
                if (ImGui.Selectable($"{actor.Name}##smad_{actor.Name}", actor == _selectedSmai))
                    _selectedSmai = actor;
        }

        ImGui.SameLine();
        CkGui.FontText($"Selected: {(_selectedSmai is null ? "<none>" : _selectedSmai.Name)}", UiFontService.Default150Percent);
        ImGui.Separator();
        ImGui.Text("Further Details here...");
    }
}