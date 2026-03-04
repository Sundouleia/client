/*
https://github.com/kawaii/Moodles/tree/main/Moodles/GameGuiProcessors
BSD 3-Clause License
Copyright (c) 2024, Kane Valentine

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
using CkCommons;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Sundouleia.Loci.Data;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;

namespace Sundouleia.Loci.Processors;
public unsafe class StatusProcessor : IDisposable
{
    private readonly ILogger<StatusProcessor> _logger;
    private readonly MainConfig _config;

    public int NumStatuses = 0;

    public StatusProcessor(ILogger<StatusProcessor> logger, MainConfig config)
    {
        _logger = logger;
        _config = config;

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "_Status", OnStatusUpdate);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_Status", OnAlcStatusRequestedUpdate);
        if(PlayerData.Available && AddonHelp.TryGetAddonByName<AtkUnitBase>("_Status", out var addon) && AddonHelp.IsAddonReady(addon))
            AddonRequestedUpdate(addon);
    }

    public void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "_Status", OnStatusUpdate);
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, "_Status", OnAlcStatusRequestedUpdate);
    }

    public void HideAll()
    {
        if(!PlayerData.Available)
            return;

        if(AddonHelp.TryGetAddonByName<AtkUnitBase>("_Status", out var addon) && AddonHelp.IsAddonReady(addon))
        {
            var validStatuses = LociManager.GetStatusManager(PlayerData.NameWithWorld).Statuses;
            UpdateStatus(addon, validStatuses, NumStatuses, true);
        }
    }

    // Func helper to get around 7.4's internal AddonArgs while removing ArtificialAddonArgs usage 
    private void OnAlcStatusRequestedUpdate(AddonEvent t, AddonArgs args)
        => AddonRequestedUpdate((AtkUnitBase*)args.Addon.Address);
    private void OnStatusUpdate(AddonEvent type, AddonArgs args)
    {
        if(!PlayerData.Available)
            return;
        if(!_config.CanLociModifyUI())
            return;
        
        var validStatuses = LociManager.GetStatusManager(PlayerData.NameWithWorld).Statuses;
        UpdateStatus((AtkUnitBase*)args.Addon.Address, validStatuses, NumStatuses);
    }

    private void AddonRequestedUpdate(AtkUnitBase* addonBase)
    {
        if (addonBase is null || !AddonHelp.IsAddonReady(addonBase) || _config.CanLociModifyUI())
            return;
        
        NumStatuses = 0;
        for (var i = 25; i >= 1; i--)
        {
            var c = addonBase->UldManager.NodeList[i];
            if (c->IsVisible())
                NumStatuses++;
        }
    }

    public void UpdateStatus(AtkUnitBase* addon, IEnumerable<LociStatus> statuses, int statusCnt, bool hideAll = false)
    {
        if (addon is null || !AddonHelp.IsAddonReady(addon))
            return;

        int baseCnt;
        if(LociProcessor.NewMethod)
            baseCnt = 25 - statusCnt;
        else
        {
            baseCnt = 25 - PlayerData.StatusList.Count(x => x.StatusId != 0);
            if(Svc.Condition[ConditionFlag.Mounted])
                baseCnt--;
        }

        // Update visibility
        for (var i = baseCnt; i >= 1; i--)
        {
            var c = addon->UldManager.NodeList[i];
            if (c->IsVisible())
                c->NodeFlags ^= NodeFlags.Visible;
        }

        // if we are to hide all, keep hidden.
        if (hideAll)
            return;

        // Otherwise, update icons
        foreach(var x in statuses)
        {
            if(baseCnt < 1) break;
            var rem = x.ExpiresAt - LociUtils.Time;
            if(rem > 0)
            {
                SetIcon(addon, baseCnt, x);
                baseCnt--;
            }
        }
    }

    private void SetIcon(AtkUnitBase* addon, int index, LociStatus status)
    {
        var container = addon->UldManager.NodeList[index];
        LociProcessor.SetIcon(addon, container, status);
    }
}
