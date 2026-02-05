using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.ModularActor;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class SMAManagerUI : WindowMediatorSubscriberBase
{
    // Some config, probably.
    private readonly SMAFileHandler _handler;
    private readonly SMAFileManager _manager;
    private readonly TutorialService _guides;

    private ModularActorData? _selectedSmad;
    private ModularActorBase? _selectedSmab;
    private ModularActorOutfit? _selectedSmao;
    private ModularActorItem? _selectedSmai;

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
        DrawElements("smad", _manager.SMAD, ref _selectedSmad);
        
        ImGui.SameLine();
        using var _ = ImRaii.Group();
        
        CkGui.FontText($"Selected: {(_selectedSmad is null ? "<none>" : _selectedSmad.Name)}", UiFontService.Default150Percent);
        if (_selectedSmad is not { } cur)
            return;

        var IsValid = cur.FileMeta?.IsValid() ?? false;
        CkGui.IconText(FAI.ExclamationTriangle, IsValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool($"File is {(IsValid ? "Valid" : "Invalid")}", IsValid);
        CkGui.TextLineSeparatorV();
        var canSave = cur.ValidForSaving();
        CkGui.IconText(FAI.ExclamationTriangle, IsValid ? ImGuiColors.ParsedGreen : ImGuiColors.DPSRed);
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool($"Item is {(IsValid ? "Valid" : "Invalid")} for Saving.", IsValid);

        ImGui.Separator();
        DrawFileMeta(cur.FileMeta);
        DrawCoreHeader(cur);
        DrawAllowedHashes(cur.Base);

        CkGui.IconText(FAI.Copyright);
        CkGui.TextInline("C+ Data?");
        CkGui.BooleanToColoredIcon(cur.CPlusData.Length > 0);

        DrawPenumbra(cur);
        DrawGlamourState(cur);

        // Edits
    }

    private void DrawSMABase()
    {
        using var tab = ImRaii.TabItem("SMABase");
        if (!tab) return;

        using var id = ImRaii.PushId("smab_info");
        DrawElements("smab", _manager.Bases, ref _selectedSmab);

        ImGui.SameLine();
        using var _ = ImRaii.Group();

        CkGui.FontText($"Selected: {(_selectedSmab is null ? "<none>" : _selectedSmab.Name)}", UiFontService.Default150Percent);
        if (_selectedSmab is not { } cur)
            return;

        var IsValid = cur.FileMeta?.IsValid() ?? false;
        CkGui.IconText(FAI.ExclamationTriangle, IsValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool($"File is {(IsValid ? "Valid" : "Invalid")}", IsValid);
        CkGui.TextLineSeparatorV();
        var canSave = cur.ValidForSaving();
        CkGui.IconText(FAI.ExclamationTriangle, IsValid ? ImGuiColors.ParsedGreen : ImGuiColors.DPSRed);
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool($"Item is {(IsValid ? "Valid" : "Invalid")} for Saving.", IsValid);

        ImGui.Separator();
        DrawFileMeta(cur.FileMeta);
        DrawCoreHeader(cur);
        DrawAllowedHashes(cur);

        CkGui.IconText(FAI.Copyright);
        CkGui.TextInline("C+ Data?");
        CkGui.BooleanToColoredIcon(cur.CPlusData.Length > 0);

        DrawPenumbra(cur);
        DrawGlamourState(cur);

        // Edits
    }

    private void DrawSMAOutfit()
    {
        using var tab = ImRaii.TabItem("SMAOutfit");
        if (!tab) return;

        using var id = ImRaii.PushId("smao_info");
        DrawElements("smao", _manager.Outfits, ref _selectedSmao);

        ImGui.SameLine();
        using var _ = ImRaii.Group();

        CkGui.FontText($"Selected: {(_selectedSmao is null ? "<none>" : _selectedSmao.Name)}", UiFontService.Default150Percent);
        if (_selectedSmao is not { } cur)
            return;

        var IsValid = cur.FileMeta?.IsValid() ?? false;
        CkGui.IconText(FAI.ExclamationTriangle, IsValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool($"File is {(IsValid ? "Valid" : "Invalid")}", IsValid);
        CkGui.TextLineSeparatorV();
        var canSave = cur.ValidForSaving();
        CkGui.IconText(FAI.ExclamationTriangle, IsValid ? ImGuiColors.ParsedGreen : ImGuiColors.DPSRed);
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool($"Item is {(IsValid ? "Valid" : "Invalid")} for Saving.", IsValid);

        ImGui.Separator();
        DrawFileMeta(cur.FileMeta);
        DrawCoreHeader(cur);

        CkGui.IconText(FAI.Copyright);
        CkGui.TextInline("C+ Data?");
        CkGui.BooleanToColoredIcon(cur.CPlusData.Length > 0);

        DrawPenumbra(cur);
        DrawGlamourState(cur);

        // Edits
    }

    private void DrawSMAItem()
    {
        using var tab = ImRaii.TabItem("SMAItem");
        if (!tab) return;

        using var id = ImRaii.PushId("smai_info");

        DrawElements("smai", _manager.Items, ref _selectedSmai);

        ImGui.SameLine();
        using var _ = ImRaii.Group();
        
        CkGui.FontText($"Selected: {(_selectedSmai is null ? "<none>" : _selectedSmai.Name)}", UiFontService.Default150Percent);
        if (_selectedSmai is not { } cur)
            return;

        var IsValid = cur.FileMeta?.IsValid() ?? false;
        CkGui.IconText(FAI.ExclamationTriangle, IsValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool($"File is {(IsValid ? "Valid" : "Invalid")}", IsValid);
        CkGui.TextLineSeparatorV();
        var canSave = cur.ValidForSaving();
        CkGui.IconText(FAI.ExclamationTriangle, IsValid ? ImGuiColors.ParsedGreen : ImGuiColors.DPSRed);
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool($"Item is {(IsValid ? "Valid" : "Invalid")} for Saving.", IsValid);

        ImGui.Separator();
        DrawFileMeta(cur.FileMeta);
        DrawCoreHeader(cur);
        DrawPenumbra(cur);
        DrawGlamourState(cur);

        // Edits
    }

    private void DrawElements<T>(string id, List<T> itemsToDraw, ref T? selected) where T : ModularActorElement
    {
        using var _ = ImRaii.ListBox("##owneditems" + id, new Vector2(125, ImGui.GetContentRegionAvail().Y));
        if (!_) return;

        foreach (var item in itemsToDraw)
            if (ImGui.Selectable($"{item.Name}##{item.Id}", item == selected))
                selected = item;
    }

    private void DrawFileMeta(SMAFileMeta? meta)
    {
        if (meta is null)
            return;

        if (!ImGui.CollapsingHeader("File Meta"))
            return;

        CkGui.IconText(FAI.File);
        CkGui.TextInline("Path:");
        CkGui.ColorTextInline(meta.FilePath, ImGuiColors.DalamudViolet);

        CkGui.IconText(FAI.UserSecret);
        CkGui.TextInline("FileHash:");
        CkGui.ColorTextInline(meta.DataHash, ImGuiColors.DalamudViolet);

        CkGui.IconText(FAI.IdCard);
        CkGui.TextInline("Name & ID:");
        CkGui.ColorTextInline($"{meta.Name} ({meta.Id})", ImGuiColors.DalamudViolet);

        if (meta is SMABaseFileMeta baseMeta)
        {
            CkGui.IconText(FAI.Key);
            CkGui.TextInline("Private Key:");
            CkGui.ColorTextInline(string.IsNullOrEmpty(baseMeta.PrivateKey) ? "<None Set>" : baseMeta.PrivateKey, ImGuiColors.DalamudViolet);
            CkGui.HelpText("Used to decrypt protected files!", true);

            CkGui.IconText(FAI.Key);
            CkGui.TextInline("Password:");
            CkGui.ColorTextInline(string.IsNullOrEmpty(baseMeta.Password) ? "<None Set>" : baseMeta.Password, ImGuiColors.DalamudViolet);
            CkGui.HelpText("Used to obtain the private key from the server, and can be shared to others you want to give access to.");
        }
    }

    private void DrawCoreHeader(ModularActorElement element)
    {
        if (!ImGui.CollapsingHeader("Element Info"))
            return;

        if (element is ModularActorBase smab)
        {
            CkGui.IconText(FAI.Link);
            CkGui.TextInline("Parent SMAD:");
            CkGui.ColorTextInline(smab.Parent is null ? "<None Set>" : smab.Parent.Name, ImGuiColors.DalamudYellow);
        }

        CkGui.IconText(FAI.AddressBook);
        CkGui.TextInline("Actor Type:");
        CkGui.ColorTextInline(element.ActorKind.ToString(), ImGuiColors.DalamudYellow);

        CkGui.IconText(FAI.IdCard);
        CkGui.TextInline("Name & ID:");
        CkGui.ColorTextInline($"{element.Name} ({element.Id})", ImGuiColors.DalamudYellow);

        CkGui.IconText(FAI.StickyNote);
        CkGui.TextInline("Description:");
        ImUtf8.SameLineInner();
        CkGui.ColorTextWrapped(element.Description, ImGuiColors.DalamudYellow);
    }

    private void DrawPenumbra(ModularActorElement element)
    {
        if (!ImGui.CollapsingHeader("Penumbra Data"))
            return;

        CkGui.IconText(FAI.FilePen);
        CkGui.TextInline("ManipString?");
        CkGui.BooleanToColoredIcon(string.IsNullOrEmpty(element.ManipString));

        var replacements = element.FileReplacements;

        using var node = ImRaii.TreeNode($"{replacements.Count} Replacements##modfilereplacements");
        if (!node) return;

        using var t = ImRaii.Table("mods_table", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!t) return;

        ImGui.TableSetupColumn("path");
        ImGui.TableSetupColumn("Replacement");
        ImGui.TableHeadersRow();

        foreach (var (path, replacement) in replacements)
        {
            ImGui.TableNextColumn();
            ImGui.Text(path);
            ImGui.TableNextColumn();
            CkGui.IconText(FAI.ArrowRight, ImGuiColors.DalamudViolet);
            CkGui.TextInline(replacement);
            ImGui.TableNextRow();
        }
    }

    private void DrawGlamourState(ModularActorElement element)
    {
        using var node = ImRaii.TreeNode("Glamour State");
        if (!node) return;

        using var _ = CkRaii.FramedChildPaddedWH("##glamNode", ImGui.GetContentRegionAvail(), 0, SundColor.Gold.Uint());
        using var font = ImRaii.PushFont(UiBuilder.MonoFont);
        ImUtf8.TextWrapped(element.GlamourState.ToString() ?? string.Empty);
    }

    // Pass in element as we will make this modifiable later!
    private void DrawAllowedHashes(ModularActorBase? smab)
    {
        if (smab is null)
            return;

        CkGui.IconText(FAI.Bars);
        ImUtf8.SameLineInner();
        using var _ = ImRaii.TreeNode("Allowed Hashes");
        if (!_) return;

        foreach (var allowance in smab.AllowedHashes)
            ImGui.BulletText(allowance);
    }
}