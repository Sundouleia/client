using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs;

namespace Sundouleia.Interop;

public sealed class IpcCallerCustomize : IIpcCaller
{
    // Version Checks.
    private readonly ICallGateSubscriber<(int, int)> ApiVersion;

    // Event Calls.
    public readonly ICallGateSubscriber<ushort, Guid, object>   OnProfileUpdate;

    // API Getters
    private readonly ICallGateSubscriber<ushort, (int, Guid?)>       GetActiveProfile; // get the active profile of a User via object index.
    private readonly ICallGateSubscriber<Guid, (int, string?)>       GetProfileById; // obtain that active profiles dataString by GUID.

    // API Enactors
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)>  SetTempProfile; // set a temp profile for a character using their json and object idx. Returns the GUID.
    private readonly ICallGateSubscriber<Guid, int>                     DelTempProfile; // Revert via cached GUID.
    private readonly ICallGateSubscriber<ushort, int>                   RevertUser; // revert via object index.

    private readonly ILogger<IpcCallerCustomize> _logger;

    public IpcCallerCustomize(ILogger<IpcCallerCustomize> logger)
    {
        _logger = logger;
        // API Version Check
        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        // API Events
        OnProfileUpdate = Svc.PluginInterface.GetIpcSubscriber<ushort, Guid, object>("CustomizePlus.Profile.OnUpdate");
        // API Getter Functions
        GetActiveProfile = Svc.PluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        GetProfileById = Svc.PluginInterface.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        // API Enactor Functions
        SetTempProfile = Svc.PluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        DelTempProfile = Svc.PluginInterface.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");
        RevertUser = Svc.PluginInterface.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");

        CheckAPI();
    }

    public void Dispose()
    { }

    public static bool APIAvailable { get; private set; } = false;
    
    public void CheckAPI()
    {
        try
        {
            var version = ApiVersion.InvokeFunc();
            APIAvailable = (version.Item1 == 6 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    /// <summary>
    ///     Obtains the active profile data of an actor by it's pointer.
    /// </summary>
    /// <returns> The string in base64 containing the c+ profile data. </returns>
    public async Task<string?> GetActiveProfileByPtr(nint sundesmoPtr)
    {
        if (!APIAvailable) return null;
        var profileStr = await Svc.Framework.RunOnFrameworkThread(() =>
        {
            // Only accept requests to obtain profiles for players.
            if (Svc.Objects.CreateObjectReference(sundesmoPtr) is { } obj && obj is ICharacter)
            {
                var res = GetActiveProfile.InvokeFunc(obj.ObjectIndex);
                _logger.LogTrace($"GetActiveProfile for [{obj.Name}] returned with EC: [{res.Item1}]", LoggerType.IpcCustomize);
                
                if (res.Item1 != 0 || res.Item2 is null) 
                    return string.Empty;
                // get the valid data by ID.
                return GetProfileById.InvokeFunc(res.Item2.Value).Item2;
            }
            // default return.
            return string.Empty;
        }).ConfigureAwait(false);

        if (string.IsNullOrEmpty(profileStr))
            return string.Empty;
        // return the valid profile string.
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(profileStr));
    }

    public async Task<Guid> ApplyTempProfile(PlayerHandler handler, string profileData)
    {
        if (!APIAvailable || handler.Address == IntPtr.Zero) return Guid.Empty;

        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            var decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(profileData));
            _logger.LogDebug($"TempProfile applied to {handler.PlayerName})", LoggerType.IpcGlamourer);
            if (string.IsNullOrEmpty(profileData))
            {
                RevertUser.InvokeFunc(handler.ObjIndex);
                return Guid.Empty;
            }
            else
            {
                return SetTempProfile.InvokeFunc(handler.ObjIndex, decodedScale).Item2 ?? Guid.Empty;
            }
        }).ConfigureAwait(false);
    }


    /// <summary>
    ///     Sets the temporary profile for the given sundesmo. <para />
    /// </summary>
    public async Task<Guid> ApplyTempProfile(PlayerOwnedHandler handler, string profileData)
    {
        if (!APIAvailable || handler.Address == IntPtr.Zero) return Guid.Empty;

        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            var decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(profileData));
            _logger.LogDebug($"TempProfile applied to {handler.ObjectName})", LoggerType.IpcGlamourer);
            if (string.IsNullOrEmpty(profileData))
            {
                RevertUser.InvokeFunc(handler.ObjIndex);
                return Guid.Empty;
            }
            else
            {
                return SetTempProfile.InvokeFunc(handler.ObjIndex, decodedScale).Item2 ?? Guid.Empty;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Deletes a temporary profile by it's ID. This allows us to not need to worry 
    ///     about object references and just use the stored GUID we had when we applied it. <para />
    ///     Effectively resets the customize state for someone.
    /// </summary>
    public async Task RevertTempProfile(Guid? profileId)
    {
        if (!APIAvailable || profileId is null) return;
        await Svc.Framework.RunOnFrameworkThread(() => DelTempProfile.InvokeFunc(profileId.Value)).ConfigureAwait(false);
    }
}
