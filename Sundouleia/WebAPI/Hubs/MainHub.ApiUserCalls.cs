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
    public async Task<HubResponse<List<ValidFileHash>>> UserPushIpcFull(PushIpcFull dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<ValidFileHash>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<ValidFileHash>>>(nameof(UserPushIpcFull), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse<List<ValidFileHash>>> UserPushIpcMods(PushIpcMods dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<ValidFileHash>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<ValidFileHash>>>(nameof(UserPushIpcMods), dto).ConfigureAwait(false);
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

    // --- Loci Updates ---
    public async Task<HubResponse> UserPushLociData(PushLociData dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserPushLociData), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserPushLociStatuses(PushLociStatuses dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserPushLociStatuses), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserPushLociPresets(PushLociPresets dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserPushLociPresets), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserPushStatusModified(PushStatusModified dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserPushStatusModified), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserPushPresetModified(PushPresetModified dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserPushPresetModified), dto).ConfigureAwait(false);
    }

    // -- Other Updates ---
    public async Task<HubResponse> UserSetAlias(AliasUpdate dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserSetAlias), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserSetVanity(VanityUpdate dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserSetVanity), dto).ConfigureAwait(false);
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

    public async Task<HubResponse<List<SundesmoRequest>>> UserSendRequests(CreateRequests dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<SundesmoRequest>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<SundesmoRequest>>>(nameof(UserSendRequests), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserCancelRequest(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserCancelRequest), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserCancelRequests(UserListDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserCancelRequests), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse<AddedUserPair>> UserAcceptRequest(RequestResponse dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<AddedUserPair>(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse<AddedUserPair>>(nameof(UserAcceptRequest), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse<List<AddedUserPair>>> UserAcceptRequests(RequestResponses dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<AddedUserPair>>(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse<List<AddedUserPair>>>(nameof(UserAcceptRequests), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserRejectRequest(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserRejectRequest), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserRejectRequests(UserListDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserRejectRequests), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserRemovePair(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserRemovePair), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserRemovePairs(UserListDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserRemovePairs), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserPersistPair(UserDto dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserPersistPair), dto).ConfigureAwait(false);
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

    public async Task<HubResponse> UserApplyLociData(ApplyLociDataById dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserApplyLociData), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserApplyLociStatusTuples(ApplyLociStatus dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserApplyLociStatusTuples), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserRemoveLociData(RemoveLociData dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError); ;
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserRemoveLociData), dto).ConfigureAwait(false);
    }

    // -- Permission Changes ---
    public async Task<HubResponse> ChangeGlobalPerm(string propName, object newValue)
        => await UserChangeGlobalsSingle(new(OwnUserData, propName, newValue));

    public async Task<HubResponse> UserChangeGlobalsSingle(ChangeGlobalPerm dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeGlobalsSingle), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserChangeAllGlobals(GlobalPerms dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeAllGlobals), dto);
    }

    public async Task<HubResponse> ChangeUniquePerm(UserData user, string propName, object newValue)
        => await UserChangeUniquePerm(new(user, propName, newValue));

    public async Task<HubResponse> UserChangeUniquePerm(ChangeUniquePerm dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeUniquePerm), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserChangeUniquePerms(ChangeUniquePerms dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeUniquePerms), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserChangeAllUnique(ChangeAllUnique dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserChangeAllUnique), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserBulkChangeUniquePerm(BulkChangeUniquePerm dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserBulkChangeUniquePerm), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserBulkChangeUniquePerms(BulkChangeUniquePerms dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserBulkChangeUniquePerms), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UserBulkChangeAllUnique(BulkChangeAllUnique dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserBulkChangeAllUnique), dto).ConfigureAwait(false);
    }


    // --- Chat and Radar Exchanges ---
    public async Task<HubResponse> UserSendChatDM(DirectChatMessage message)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UserSendChatDM), message).ConfigureAwait(false);
    }

    public async Task<HubResponse<LocationUpdateResult>> UpdateLocation(LocationUpdate dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<LocationUpdateResult>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<LocationUpdateResult>>(nameof(UpdateLocation), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse<List<LoggedRadarChatMessage>>> RadarChatJoin(RadarChatMember joinDto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<LoggedRadarChatMessage>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<LoggedRadarChatMessage>>>(nameof(RadarChatJoin), joinDto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarChatPermissionChange(RadarChatMember updateDto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarChatPermissionChange), updateDto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarSendChat(SentRadarMessage messageDto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarSendChat), messageDto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarChatLeave()
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarChatLeave)).ConfigureAwait(false);
    }

    public async Task<HubResponse<List<RadarMember>>> RadarAreaJoin(RadarMember joinDto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<RadarMember>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<RadarMember>>>(nameof(RadarAreaJoin), joinDto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarAreaPermissionChange(RadarMember updateDto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarAreaPermissionChange), updateDto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarAreaLeave()
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarAreaLeave)).ConfigureAwait(false);
    }

    public async Task<HubResponse<List<RadarGroupMember>>> RadarGroupJoin(RadarGroupMember joinDto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<RadarGroupMember>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<RadarGroupMember>>>(nameof(RadarGroupJoin), joinDto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarGroupPermissionChange(RadarGroupMember updateDto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarGroupPermissionChange), updateDto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RadarGroupLeave()
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RadarGroupLeave)).ConfigureAwait(false);
    }

    // --- SMA File Sharing ---
    public async Task<HubResponse<SMABFileInfo>> AccessFile(SMABFileAccess dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<SMABFileInfo>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<SMABFileInfo>>(nameof(AccessFile), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse<List<string>>> GetAllowedHashes(Guid FileId)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt<List<string>>(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse<List<string>>>(nameof(GetAllowedHashes), FileId).ConfigureAwait(false);
    }

    public async Task<HubResponse> CreateProtectedSMAB(NewSMABFile dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(CreateProtectedSMAB), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UpdateFileDataHash(SMABDataUpdate dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UpdateFileDataHash), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UpdateFilePassword(SMABDataUpdate dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UpdateFilePassword), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UpdateAllowedHashes(SMABAccessUpdate dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UpdateAllowedHashes), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UpdateAllowedUids(SMABAccessUpdate dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UpdateAllowedUids), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> UpdateExpireTime(SMABExpireTime dto)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(UpdateExpireTime), dto).ConfigureAwait(false);
    }

    public async Task<HubResponse> RemoveProtectedFile(Guid FileId)
    {
        if (!IsConnected) return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NetworkError);
        return await _hubConnection!.InvokeAsync<HubResponse>(nameof(RemoveProtectedFile), FileId).ConfigureAwait(false);
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
