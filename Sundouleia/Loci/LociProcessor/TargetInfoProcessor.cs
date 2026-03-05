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
using CkCommons.Helpers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Statuses;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Penumbra.GameData.Interop;
using Sundouleia.Loci.Data;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Loci.Processors;

public unsafe class TargetInfoProcessor
{
    private readonly ILogger<TargetInfoProcessor> _logger;
    private readonly MainConfig _config;

    public int NumStatuses = 0;
    public TargetInfoProcessor(ILogger<TargetInfoProcessor> logger, MainConfig config)
    {
        _logger = logger;
        _config = config;
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreUpdate, "_TargetInfo", OnTargetInfoUpdate);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, "_TargetInfo", OnTargetInfoRequestedUpdate);
        unsafe
        {
            if (PlayerData.Available && AddonHelp.TryGetAddonByName<AtkUnitBase>("_TargetInfo", out var addon) && AddonHelp.IsAddonReady(addon))
                AddonRequestedUpdate(addon);
        }
    }

    public void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PreUpdate, "_TargetInfo", OnTargetInfoUpdate);
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, "_TargetInfo", OnTargetInfoRequestedUpdate);
    }

    public unsafe void HideAll()
    {
        if(AddonHelp.TryGetAddonByName<AtkUnitBase>("_TargetInfo", out var addon) && AddonHelp.IsAddonReady(addon))
            UpdateAddon((nint)addon, true);
    }

    // Func helper to get around 7.4's internal AddonArgs while removing ArtificialAddonArgs usage
    private unsafe void OnTargetInfoRequestedUpdate(AddonEvent t, AddonArgs args)
        => AddonRequestedUpdate((AtkUnitBase*)args.Addon.Address);

    private unsafe void AddonRequestedUpdate(AtkUnitBase* addonBase)
    {
        if(addonBase is not null && AddonHelp.IsAddonReady(addonBase))
        {
            NumStatuses = 0;
            for(var i = 32; i >= 3; i--)
            {
                // Ensure we count the number of vanilla statuses.
                var c = addonBase->UldManager.NodeList[i];
                if(c->IsVisible())
                    NumStatuses++;
            }
        }
        _logger.LogTrace($"TargetInfo Requested update: {NumStatuses}", LoggerType.LociProcessors);
    }

    private void OnTargetInfoUpdate(AddonEvent type, AddonArgs args)
    {
        if (!PlayerData.Available)
            return;
        if (!_config.CanLociModifyUI())
            return;
        UpdateAddon(args.Addon.Address);
    }

    public unsafe void UpdateAddon(nint addonAddr, bool hideAll = false)
    {
        var addon = (AtkUnitBase*)addonAddr;
        var ts = TargetSystem.Instance();
        var target = ts->SoftTarget is not null ? ts->SoftTarget : ts->Target;
        if (target is null || !target->IsCharacter() || target->ObjectKind is not ObjectKind.Pc)
            return;

        if (addon is null || !AddonHelp.IsAddonReady(addon))
            return;

        // Get the base count by combining the statuses from Moodles with the vanilla ones.
        var baseCnt = 32 - NumStatuses;

        for(var i = baseCnt; i >= 3; i--)
        {
            var c = addon->UldManager.NodeList[i];
            if(c->IsVisible())
                c->NodeFlags ^= NodeFlags.Visible;
        }

        if (hideAll)
            return;

        var sm = ((Character*)target)->GetManager();
        foreach (var x in sm.Statuses)
        {
            if (baseCnt < 3)
                break;

            if (x.ExpiresAt - LociUtils.Time > 0)
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
