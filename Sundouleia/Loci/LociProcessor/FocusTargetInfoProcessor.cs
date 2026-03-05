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
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Sundouleia.Loci.Data;
using Sundouleia.PlayerClient;

namespace Sundouleia.Loci.Processors;
public unsafe class FocusTargetInfoProcessor
{
    private readonly ILogger<FocusTargetInfoProcessor> _logger;
    private readonly MainConfig _config;
    private readonly LociMemory _memory;

    private int NumStatuses = 0;

    public FocusTargetInfoProcessor(ILogger<FocusTargetInfoProcessor> logger, MainConfig config, LociMemory memory)
    {
        _logger = logger;
        _config = config;
        _memory = memory;

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "_FocusTargetInfo", OnFocusTargetInfoUpdate);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_FocusTargetInfo", OnFocusTargetInfoRequestedUpdate);
        if(PlayerData.Available && AddonHelp.TryGetAddonByName<AtkUnitBase>("_FocusTargetInfo", out var addon) && AddonHelp.IsAddonReady(addon))
                AddonRequestedUpdate(addon);
        }

    public void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "_FocusTargetInfo", OnFocusTargetInfoUpdate);
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, "_FocusTargetInfo", OnFocusTargetInfoRequestedUpdate);
    }

    public void HideAll()
    {
        if(AddonHelp.TryGetAddonByName<AtkUnitBase>("_FocusTargetInfo", out var addon) && AddonHelp.IsAddonReady(addon))
            UpdateAddon(addon, true);
    }

    // Func helper to get around 7.4's internal AddonArgs while removing ArtificialAddonArgs usage 
    private void OnFocusTargetInfoRequestedUpdate(AddonEvent t, AddonArgs args)
        => AddonRequestedUpdate((AtkUnitBase*)args.Addon.Address);

    private void OnFocusTargetInfoUpdate(AddonEvent type, AddonArgs args)
    {
        if(!PlayerData.Available) return;
        if(_config.CanLociModifyUI())
            UpdateAddon((AtkUnitBase*)args.Addon.Address);
    }

    private void AddonRequestedUpdate(AtkUnitBase* addonBase)
    {
        if (addonBase is not null && AddonHelp.IsAddonReady(addonBase))
        {
            NumStatuses = 0;
            for (var i = 8; i >= 4; i--)
            {
                var c = addonBase->UldManager.NodeList[i];
                if (c->IsVisible())
                    NumStatuses++;
            }
        }
        _logger.LogTrace($"FocusTarget Requested update: {NumStatuses}", LoggerType.LociProcessors);
    }

    public unsafe void UpdateAddon(AtkUnitBase* addon, bool hideAll = false)
    {
        var target = TargetSystem.Instance()->FocusTarget;
        if (target is null || !target->IsCharacter() || target->ObjectKind is not ObjectKind.Pc)
            return;

        if (addon is null || !AddonHelp.IsAddonReady(addon))
            return;
        
        // Determine the base count by combining the Moodles statuses with the statuses from the base game.
        var baseCnt = 8 - NumStatuses;
        for (var i = baseCnt; i >= 4; i--)
        {
            var c = addon->UldManager.NodeList[i];
            if(c->IsVisible())
                c->NodeFlags ^= NodeFlags.Visible;
        }

        if (hideAll)
            return;

        // Update the displays.
        var sm = ((Character*)target)->GetManager();
        foreach (var x in sm.Statuses)
        {
            if (x.Type is StatusType.Special)
                continue;

            if (baseCnt < 4)
                break;
                    
            var rem = x.ExpiresAt - LociUtils.Time;
            if (rem > 0)
            {
                SetIcon(addon, baseCnt, x);
                baseCnt--;
            }
        }
    }

    private unsafe void SetIcon(AtkUnitBase* addon, int index, LociStatus status)
    {
        var container = addon->UldManager.NodeList[index];
        LociProcessor.SetIcon(addon, container, status);
    }
}
