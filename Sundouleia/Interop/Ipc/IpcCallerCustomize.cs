using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs.Handlers;
using Sundouleia.Services.Mediator;

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
    private readonly SundouleiaMediator _mediator;

    public IpcCallerCustomize(ILogger<IpcCallerCustomize> logger, SundouleiaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
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

        OnProfileUpdate.Subscribe(ProfileUpdated);
        CheckAPI();
    }

    public static bool APIAvailable { get; private set; } = false;

    public void Dispose()
        => OnProfileUpdate.Unsubscribe(ProfileUpdated);
    
    private void ProfileUpdated(ushort objIdx, Guid id)
    {
        // This can be safely accessed. It is called in the framework thread.
        var obj = Svc.Objects[objIdx];
        // we dont care if it is not a player owned object.
        if (obj is null || obj.ObjectKind != ObjectKind.Player)
            return;
        // publish the address and the new profile ID.
        _mediator.Publish(new CustomizeProfileChange(obj.Address, id));
    }

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
    public async Task<string?> GetActiveProfileByPtr(nint kinksterPtr)
    {
        if (!APIAvailable) return null;
        var profileStr = await Svc.Framework.RunOnFrameworkThread(() =>
        {
            // Only accept requests to obtain profiles for players.
            if (Svc.Objects.CreateObjectReference(kinksterPtr) is { } obj && obj is ICharacter)
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

    /// <summary>
    ///     Sets the temporary profile for the given sundesmo. <para />
    /// </summary>
    /// <returns> The GUID applied if successful. </returns>
    public async Task<Guid?> ApplyTempProfile(SundesmoHandler sundesmo, string profileData)
    {
        if (!APIAvailable || sundesmo.PairObject is not { } visibleObj) return null;

        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            var decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(profileData));
            _logger.LogTrace($"Applying Profile to {visibleObj.Name}");
            // revert the character if the new data to set was empty.
            if (string.IsNullOrEmpty(profileData))
            {
                RevertUser.InvokeFunc(visibleObj.ObjectIndex);
                return null;
            }
            // Otherwise set the new profile data.
            else
            {
                return SetTempProfile.InvokeFunc(visibleObj.ObjectIndex, decodedScale).Item2;
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
