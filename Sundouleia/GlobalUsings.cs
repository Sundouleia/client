/// Global Usings
global using Dalamud.Utility;
global using Newtonsoft.Json;
global using Newtonsoft.Json.Linq;
global using Microsoft.Extensions.Logging;
global using System.Collections.Concurrent;
global using System.Collections;
global using System.Diagnostics;
global using System.Text;
global using System.Numerics;
global using SundouleiaAPI.Enums;
global using SundouleiaAPI;
global using CFlags = Dalamud.Bindings.ImGui.ImGuiComboFlags;
global using WFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;
global using FAI = Dalamud.Interface.FontAwesomeIcon;

// MERV! DON'T SUMMON BAGAGWA!
global using Bagagwa = System.Exception;

// Used for Tuple-Based IPC calls and associated data transfers.
global using LociStatusInfo = (
    int Version,
    System.Guid GUID,
    int IconID,
    string Title,
    string Description,
    string CustomVFXPath,       // What VFX to show on application.
    long ExpireTicks,           // Permanent if -1, referred to as 'NoExpire' in LociStatus
    SundouleiaAPI.StatusType Type,  // Loci StatusType enum.
    int Stacks,                 // Usually 1 when no stacks are used.
    int StackSteps,             // How many stacks to add per reapplication.
    uint Modifiers,             // What can be customized, casted to uint from Modifiers (Dalamud IPC Rules)
    System.Guid ChainedStatus,  // What status is chained to this one.
    SundouleiaAPI.ChainTrigger ChainTrigger, // What triggers the chained status.
    string Applier,             // Who applied the loci.
    string Dispeller,           // When set, only this person can dispel your loci.
    bool Permanent              // Referred to as 'Sticky' (Legacy)
);

global using LociPresetInfo = (
    System.Guid GUID,
    System.Collections.Generic.List<System.Guid> Statuses,
    byte ApplicationType,
    string Title,
    string Description
);

global using MoodlesStatusInfo = (
    int Version,
    System.Guid GUID,
    int IconID,
    string Title,
    string Description,
    string CustomVFXPath,               // What VFX to show on application.
    long ExpireTicks,                   // Permanent if -1, referred to as 'NoExpire' in MoodleStatus
    SundouleiaAPI.StatusType Type,        // Moodles StatusType enum.
    int Stacks,                         // Usually 1 when no stacks are used.
    int StackSteps,                     // How many stacks to add per reapplication.
    uint Modifiers,                     // What can be customized, casted to uint from Modifiers (Dalamud IPC Rules)
    System.Guid ChainedStatus,          // What status is chained to this one.
    SundouleiaAPI.ChainTrigger ChainTrigger, // What triggers the chained status.
    string Applier,                     // Who applied the moodle.
    string Dispeller,                   // When set, only this person can dispel your moodle.
    bool Permanent                      // Referred to as 'Sticky' in the Moodles UI
);

global using MoodlePresetInfo = (
    System.Guid GUID,
    System.Collections.Generic.List<System.Guid> Statuses,
    byte ApplicationType,
    string Title
);

global using MoodlesMoodleInfo = (System.Guid ID, uint IconID, string FullPath, string Title);
global using MoodlesProfileInfo = (System.Guid ID, string FullPath);