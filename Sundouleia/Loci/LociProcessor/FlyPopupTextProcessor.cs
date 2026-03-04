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
using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Sundouleia.Loci.Data;
using Sundouleia.PlayerClient;

namespace Sundouleia.Loci.Processors;

/// <summary>
///     Segment of LociProcesser responcible for FlyPopupText.
/// </summary>
public unsafe class FlyPopupTextProcessor : IDisposable
{
    private readonly ILogger<FlyPopupTextProcessor> _logger;
    private readonly MainConfig _config;
    private readonly LociMemory _memory;

    private List<FlyPopupTextData> _queue = [];

    public FlyPopupTextProcessor(ILogger<FlyPopupTextProcessor> logger, MainConfig config, LociMemory memory)
    {
        _logger = logger;
        _config = config;
        _memory = memory;

        foreach (var x in Svc.Data.GetExcelSheet<Status>())
        {
            var baseData = new IconStatusData(x.RowId, x.Name.ExtractText(), 0);
            StatusData[x.Icon] = baseData;
            for (var i = 2; i <= x.MaxStacks; i++)
            {
                StatusData[(uint)(x.Icon + i - 1)] = baseData with { StackCount = (uint)i };
            }
        }

        Svc.Framework.Update += OnTick;
    }

    public FlyPopupTextData CurrentElement = null!;
    public Dictionary<uint, IconStatusData> StatusData = [];

    public void Dispose()
    {
        Svc.Framework.Update -= OnTick;
    }

    public void Enqueue(FlyPopupTextData data)
    {
        if (_config.Current.LociFlyText)
            _queue.Add(data);
    }

    private unsafe void OnTick(IFramework _)
    {
        ProcessPopupText();
        ProcessFlyText();
        if(CurrentElement != null)
            CurrentElement = null!;

        var objManager = GameObjectManager.Instance();

        if (_queue.Count > _config.Current.LociFlyTextLimit)
        {
            _logger.LogWarning($"Queue is too large! Trimming to {_config.Current.LociFlyTextLimit} closest entities.");
            var n = _queue.RemoveAll(x =>
            {
                var obj = objManager->Objects.GetObjectByEntityId(x.OwnerEntityId);
                return obj == null || !obj->IsCharacter();
            });

            if (n > 0)
                _logger.LogInformation($"Removed {n} non-player entities", LoggerType.LociProcessors);

            _queue = _queue
                .OrderBy(x => Vector3.DistanceSquared(PlayerData.Character->Position, objManager->Objects.GetObjectByEntityId(x.OwnerEntityId)->Position))
                .Take(_config.Current.LociFlyTextLimit)
                .ToList();
        }
        
        while(_queue.TryDequeue(out var e))
        {
            Character* target = null;
            for(var i = 0; i < 200; i++)
            {
                GameObject* obj = objManager->Objects.IndexSorted[i];
                if (obj == null) continue;
                if (obj->EntityId != e.OwnerEntityId) continue;
                if (!obj->IsCharacter()) continue;

                target = (Character*)obj;
                break; // Break out of loop once found.
            }

            // Process logic for non-null target.
            if(target is not null)
            {
                _logger.LogDebug($"Processing {e.Status.Title} at {LociUtils.Frame} for {target->NameString}...");
                CurrentElement = e;
                var isMine = e.Status.Applier == PlayerData.NameWithWorld && e.IsAddition;
                FlyTextKind kind;
                if(e.Status.Type is StatusType.Negative)
                    kind = e.IsAddition ? FlyTextKind.Debuff : FlyTextKind.DebuffFading;
                else
                    kind = e.IsAddition ? FlyTextKind.Buff : FlyTextKind.BuffFading;

                if (StatusData.TryGetValue(e.Status.AdjustedIconID, out var data))
                    _memory.BattleLog_AddToScreenLogWithScreenLogKindDetour((nint)target, isMine ? PlayerData.Address : (nint)target, kind, 5, 0, 0, (int)data.StatusId, (int)data.StackCount, 0);
                else
                {
                    _logger.LogError($"Error retrieving data for icon {e.Status.IconID}, please report to developer." +
                        $"\nAt the time of getting this error, {StatusData.Count} StatusData's were registered.");
                }
                break;
            }
            else
            {
                _logger.LogDebug($"Skipping {e.Status.Title} for {e.OwnerEntityId:X8}, not found...", LoggerType.LociProcessors);
            }
        }
    }

    private void ProcessPopupText()
    {
        if (CurrentElement is null)
            return;
        if (!AddonHelp.TryGetAddonByName<AtkUnitBase>("_PopUpText", out var addon))
            return;
        
        for(var i = 1; i < addon->UldManager.NodeListCount; i++)
        {
            var candidate = addon->UldManager.NodeList[i];
            if(IsCandidateValid(candidate))
            {
                var c = candidate->GetAsAtkComponentNode()->Component;
                var sestr = new SeStringBuilder().AddText(CurrentElement.IsAddition ? "+ " : "- ").Append(LociUtils.ParseBBSeString(CurrentElement.Status.Title));
                c->UldManager.NodeList[1]->GetAsAtkTextNode()->SetText(sestr.Encode());
                c->UldManager.NodeList[2]->GetAsAtkImageNode()->LoadTexture(Svc.Texture.GetIconPath(CurrentElement.Status.AdjustedIconID), 1);
                CurrentElement = null!;
                return;
            }
        }
    }


    private void ProcessFlyText()
    {
        if (CurrentElement is null)
            return;
        if (!AddonHelp.TryGetAddonByName<AtkUnitBase>("_FlyText", out var addon))
            return;

        for(var i = 1; i < addon->UldManager.NodeListCount; i++)
        {
            var candidate = addon->UldManager.NodeList[i];
            if(IsCandidateValid(candidate))
            {
                var c = candidate->GetAsAtkComponentNode()->Component;
                var sestr = new SeStringBuilder().AddText(CurrentElement.IsAddition ? "+ " : "- ").Append(LociUtils.ParseBBSeString(CurrentElement.Status.Title));
                c->UldManager.NodeList[1]->GetAsAtkTextNode()->SetText(sestr.Encode());
                CurrentElement = null!;
                return;
            }
        }
    }

    private bool IsCandidateValid(AtkResNode* node)
    {
        if(!node->IsVisible()) return false;
        var c = node->GetAsAtkComponentNode()->Component;
        if(c->UldManager.NodeListCount < 3 || c->UldManager.NodeListCount > 4)
            return false;
        if(c->UldManager.NodeList[1]->Type != NodeType.Text)
            return false;
        if(!c->UldManager.NodeList[1]->IsVisible())
            return false;
        if(c->UldManager.NodeList[2]->Type != NodeType.Image)
            return false;
        if(!c->UldManager.NodeList[2]->IsVisible())
            return false;
        
        var text = MemoryHelper.ReadSeString(&c->UldManager.NodeList[1]->GetAsAtkTextNode()->NodeText)?.GetText();
        if(text is null || !text.StartsWith('-') && !text.StartsWith('+'))
            return false;

        // Check StatusData using the concise TryGetValue pattern
        return StatusData.TryGetValue(CurrentElement.Status.AdjustedIconID, out var data) && text.Contains(data.Name);
    }
}
