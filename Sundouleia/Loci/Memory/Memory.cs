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
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using Microsoft.Extensions.Hosting;
using Sundouleia.Loci.Processors;
using Sundouleia.PlayerClient;
using System.Runtime.InteropServices;
using System.Security.Cryptography.Xml;

namespace Sundouleia.Loci;

/// <summary>
///     Hosted service to manage the memory hooks and delegates for Sundouleia's Loci Module.
/// </summary>
public unsafe partial class LociMemory : IHostedService
{
    private readonly ILogger<LociMemory> _logger;
    private readonly MainConfig _config;
    public LociMemory(ILogger<LociMemory> logger, MainConfig config)
    {
        _logger = logger;
        _config = config;

        logger.LogInformation("Initializing Memory");
        Svc.Hook.InitializeFromAttributes(this);
        // Hook the function delegate as well.
        AtkComponentIconText_LoadIconByID = Marshal.GetDelegateForFunctionPointer<AtkComponentIconText_LoadIconByIDDelegate>(Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 41 8D 45 3D"));
        ReceiveAtkCompIconTxtEventHook.SafeEnable();
        SheApplierHook.SafeEnable();
        BattleLog_AddToScreenLogWithScreenLogKindHook.SafeEnable();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Memory hooks enabled and delegates assigned.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Safe disable-dispose all hooks, and clear funcs.
        ReceiveAtkCompIconTxtEventHook.SafeDispose();
        SheApplierHook.SafeDispose();
        BattleLog_AddToScreenLogWithScreenLogKindHook.SafeDispose();
        // Clear the function delegates.
        ReceiveAtkCompIconTxtEventHook = null!;
        SheApplierHook = null!;
        BattleLog_AddToScreenLogWithScreenLogKindHook = null!;
        AtkComponentIconText_LoadIconByID = null!;
        _logger.LogInformation("Memory hooks and delegates safely disposed and cleared.");
        return Task.CompletedTask;
    }


    // The delegate for loading an icon by its ID, used for the status icons in the target info and player info bars.
    public delegate nint AtkComponentIconText_LoadIconByIDDelegate(void* iconText, int iconId);
    // The hookable func for this delegate.
    internal static AtkComponentIconText_LoadIconByIDDelegate AtkComponentIconText_LoadIconByID = null!;

    // The delegate for receiving events when hovering over an icon in the positive, negative, or special status icons.
    public delegate void AtkComponentIconText_ReceiveEvent(nint a1, short a2, nint a3, nint a4, nint a5);
    [Signature("44 0F B7 C2 4D 8B D1", DetourName = nameof(ReceiveAtkCompIconTxtEventDetour), Fallibility = Fallibility.Auto)]
    internal static Hook<AtkComponentIconText_ReceiveEvent> ReceiveAtkCompIconTxtEventHook = null!;

    /// <summary>
    ///     Handles the detour of when we hover over an icon in our positive, negative, or special status icons.
    /// </summary>
    private void ReceiveAtkCompIconTxtEventDetour(nint a1, short a2, nint a3, nint a4, nint a5)
    {
        try
        {
            //_logger.LogDebug($"{a1:X16}, {a2}, {a3:X16}, {a4:X16}, {a5:X16}");
            if (a2 is 6)
                LociProcessor.HoveringOver = a1;

            if (a2 is 7)
                LociProcessor.HoveringOver = 0;

            // Handle Cancellation Request on Right Click
            if (a2 is 9 && LociProcessor.WasRightMousePressed)
            {
                // We dunno what status this is yet, so mark the address for next check.
                LociProcessor.CancelRequests.Add(a1);
                LociProcessor.HoveringOver = 0;
            }
        }
        catch (Bagagwa e)
        {
            _logger.LogError($"Error processing AtkCompIconTxtEventDetour: {e}");
        }
        // Ret the original, always
        ReceiveAtkCompIconTxtEventHook.Original(a1, a2, a3, a4, a5);
    }

    /// <summary>
    ///     For applying the SHE VFX.
    /// </summary>
    internal delegate nint SheApplier(string path, nint target, nint target2, float speed, char a5, UInt16 a6, char a7);
    [Signature("E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 27 B2 01", DetourName = nameof(SheApplierDetour), Fallibility = Fallibility.Auto)]
    internal static Hook<SheApplier> SheApplierHook = null!;

    private nint SheApplierDetour(string path, nint target, nint target2, float speed, char a5, UInt16 a6, char a7)
    {
        try
        {
            _logger.LogInformation($"SheApplier {path}, {target:X16}, {target2:X16}, {speed}, {a5}, {a6}, {a7}", LoggerType.LociMemory);
        }
        catch (Bagagwa e)
        {
            _logger.LogError($"Error in SheApplierDetour: {e}");
        }
        return SheApplierHook.Original(path, target, target2, speed, a5, a6, a7);
    }

    /// <summary>
    ///     Spawn a SHE VFX by the iconID of the status effect.
    /// </summary>
    internal void SpawnSHE(uint iconID, nint target, nint target2, float speed = -1.0f, char a5 = char.MinValue, UInt16 a6 = 0, char a7 = char.MinValue)
    {
        try
        {
            string smallPath = GameDataHelp.GetVfxPathByID(iconID);
            if (smallPath.IsNullOrWhitespace())
            {
                _logger.LogInformation($"Path for IconID: {iconID} is empty", LoggerType.LociSheVfx);
                return;
            }
            _logger.LogTrace($"Path for IconID: {iconID} is: {smallPath}", LoggerType.LociSheVfx);
            SpawnSHE(smallPath, target, target2, speed, a5, a6, a7);
        }
        catch (Bagagwa e)
        {
            _logger.LogError($"Error in SpawnSHE: {e}");
        }
    }

    /// <summary>
    ///     spawn a SHE VFX by its path.
    /// </summary>
    internal void SpawnSHE(string path, nint target, nint target2, float speed = -1.0f, char a5 = char.MinValue, UInt16 a6 = 0, char a7 = char.MinValue)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogInformation($"Path for SHE is empty", LoggerType.LociSheVfx);
                return;
            }

            var fullPath = GameDataHelp.GetVfxPath(path);
            _logger.LogTrace($"Path for SHE is: {fullPath}", LoggerType.LociSheVfx);
            SheApplierHook.Original(fullPath, target, target2, speed, a5, a6, a7);
        }
        catch (Bagagwa e)
        {
            _logger.LogError($"Error in SpawnSHE: {e}");
        }
    }
}
#pragma warning restore CS0649 // Ignore "Field is never assigned to" warnings for IPC fields

