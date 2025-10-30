using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia.Pairs;
using Sundouleia.Services.Configs;

namespace Sundouleia.Gui.Handlers;

// Can merge this into a "nickname editor" or something idk.
public class IdDisplayHandler
{
    private readonly ServerConfigManager _serverConfig;

    private string _editingEntityID = string.Empty;
    private string _nickEditStr = string.Empty;
    public IdDisplayHandler(ServerConfigManager serverManager)
    {
        _serverConfig = serverManager;
    }

    public bool IsEditing(string drawEntityId)
        => string.Equals(_editingEntityID, drawEntityId, StringComparison.Ordinal);

    public void ToggleEditModeForID(string drawEntityId, Sundesmo sundesmo)
    {
        if (IsEditing(drawEntityId))
            _editingEntityID = string.Empty;
        else
        {
            _editingEntityID = drawEntityId;
            _nickEditStr = sundesmo.GetNickname() ?? string.Empty;
        }
    }

    public void DrawEditor(string id, Sundesmo sundesmo, float width)
    {
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint($"##{sundesmo.UserData.UID}-nick", "Give a nickname..", ref _nickEditStr, 45, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _serverConfig.SetNickname(sundesmo.UserData.UID, _nickEditStr);
            _editingEntityID = string.Empty;
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _editingEntityID = string.Empty;

        CkGui.AttachToolTip("--COL--[ENTER]--COL-- To save" +
            "--NL----COL--[R-CLICK]--COL-- Cancel edits.", ImGuiColors.DalamudOrange);
    }

    internal void Clear()
    {
        _editingEntityID = string.Empty;
        _nickEditStr = string.Empty;
    }
}
