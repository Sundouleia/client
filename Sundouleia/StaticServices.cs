using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Sundouleia.State.Models;
using Sundouleia.Utils;
using OtterGui.Log;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sundouleia;

// An internal Static accessor for all DalamudPlugin interfaces, because im tired of interface includes.
// And the difference is negligible and its basically implied to make them static with the PluginService attribute.

/// <summary>
///     A collection of internally handled Dalamud Interface static services
/// </summary>
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
public class Svc
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] public static IPluginLog Logger { get; set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; set; } = null!;
    [PluginService] public static IAddonEventManager AddonEventManager { get; private set; }
    [PluginService] public static IAetheryteList AetheryteList { get; private set; }
    //[PluginService] public static ITitleScreenMenu TitleScreenMenu { get; private set; } = null!;
    //[PluginService] public static IBuddyList Buddies { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; set; } = null!;
    [PluginService] public static IClientState ClientState { get; set; } = null!;
    [PluginService] public static ICommandManager Commands { get; private set; }
    [PluginService] public static ICondition Condition { get; private set; }
    [PluginService] public static IContextMenu ContextMenu { get; private set; }
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] public static IDutyState DutyState { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    //[PluginService] public static IGameInventory GameInventory { get; private set; } = null!;
    //[PluginService] public static IGameNetwork GameNetwork { get; private set; } = null!;
    //[PluginService] public static IJobGauges Gauges { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Hook { get; private set; } = null!;
    [PluginService] public static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] public static IGameLifecycle GameLifeCycle { get; private set; } = null!;
    [PluginService] public static IGamepadState GamepadState { get; private set; } = null!;
    [PluginService] public static IKeyState KeyState { get; private set; } = null!;
    [PluginService] public static INotificationManager Notifications { get; private set; } = null!;
    [PluginService] public static INamePlateGui NamePlate { get; private set; } = null!;
    [PluginService] public static IObjectTable Objects { get; private set; } = null!;
    [PluginService] public static IPartyList Party { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static ITargetManager Targets { get; private set; } = null!;
    [PluginService] public static ITextureProvider Texture { get; private set; } = null!;
    [PluginService] public static IToastGui Toasts { get; private set; } = null!;
    [PluginService] public static ITextureSubstitutionProvider TextureSubstitution { get; private set; } = null!;
}
