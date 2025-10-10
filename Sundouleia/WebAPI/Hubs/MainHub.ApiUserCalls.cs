using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using Microsoft.AspNetCore.SignalR.Client;

namespace Sundouleia.WebAPI;

#pragma warning disable MA0040

public partial class MainHub
{
    // --- Data Updates ---
    public async Task<HubResponse<List<VerifiedModFile>>> UserPushIpcFull(PushIpcFull dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<VerifiedModFile>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<VerifiedModFile>>>(nameof(UserPushIpcFull), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse<List<VerifiedModFile>>> UserPushIpcMods(PushIpcMods dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<VerifiedModFile>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<VerifiedModFile>>>(nameof(UserPushIpcMods), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserPushIpcOther(PushIpcOther dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError, string.Empty);
        return await _hubConnection!.InvokeAsync<HubResponse<string>>(nameof(UserPushIpcOther), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserPushIpcSingle(PushIpcSingle dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserPushIpcSingle), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserUpdateProfileContent(ProfileContent dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserUpdateProfileContent), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserUpdateProfilePicture(ProfileImage dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserUpdateProfilePicture), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserDelete()
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserDelete)).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserNotifyIsUnloading()
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserNotifyIsUnloading)).ConfigureAwait(false);
    }


    // --- Pair/Request Interactions ---
    public async Task<HubResponse<SundesmoRequest>> UserSendRequest(CreateRequest dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<SundesmoRequest>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<SundesmoRequest>>(nameof(UserSendRequest), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserCancelRequest(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserCancelRequest), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse<AddedUserPair>> UserAcceptRequest(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<AddedUserPair>(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse<AddedUserPair>>(nameof(UserAcceptRequest), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserRejectRequest(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserRejectRequest), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserRemovePair(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserRemovePair), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserBlock(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserBlock), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserUnblock(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserUnblock), dto).ConfigureAwait(false);
    }


    // -- Permission Changes ---
    public async Task<HubResponse> ChangeGlobalPerm(string propName, bool newValue)
        => await UserChangeGlobalsSingle(new(OwnUserData, propName, newValue));

    public async Task<HubResponse> UserChangeGlobalsSingle(SingleChangeGlobal dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeGlobalsSingle), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserChangeGlobalsBulk(GlobalPerms dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeGlobalsBulk), dto);
    }

    public async Task<HubResponse> ChangeUniquePerm(UserData user, string propName, bool newValue)
        => await UserChangeUniqueSingle(new(user, propName, newValue));

    public async Task<HubResponse> UserChangeUniqueSingle(SingleChangeUnique dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeUniqueSingle), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserChangeUniqueBulk(BulkChangeUnique dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeUniqueBulk), dto).ConfigureAwait(false);
    }


    // --- Radar Exchanges ---
    public async Task<HubResponse<RadarZoneInfo>> RadarZoneJoin(RadarZoneUpdate dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<RadarZoneInfo>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<RadarZoneInfo>>(nameof(RadarZoneJoin), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarZoneLeave()
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarZoneLeave)).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarUpdateState(RadarState stateUpdate)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarUpdateState), stateUpdate).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarChatMessage(RadarChatMessage dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarChatMessage), dto).ConfigureAwait(false);
    }


    // --- Reporting ---
    public async Task<HubResponse> UserReportProfile(ProfileReport dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserReportProfile), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserReportRadar(RadarReport dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserReportRadar), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserReportChat(RadarChatReport dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserReportChat), dto).ConfigureAwait(false);
    }
}
#pragma warning restore MA0040
