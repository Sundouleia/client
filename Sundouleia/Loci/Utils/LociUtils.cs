using CkCommons;
using CkCommons.Helpers;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using Sundouleia.Loci.Data;
using Sundouleia.Pairs;
using System.Text.RegularExpressions;

namespace Sundouleia.Loci;

public unsafe static partial class LociUtils
{
    internal static Dictionary<uint, StatusIconData?> IconInfoCache = [];
    
    /// <summary>
    ///     Attempt to get an analysis of the StatusIconData from the given IconID.
    /// </summary>
    public static StatusIconData? GetIconData(uint iconID)
    {
        if (IconInfoCache.TryGetValue(iconID, out var iconInfo))
            return iconInfo;
        // Otherwise, create it or return null if not in the lookup.
        if (Svc.Data.GetExcelSheet<Status>().TryGetFirst(x => x.Icon == iconID, out var status))
        {
            var iconData = new StatusIconData(status);
            IconInfoCache[iconID] = iconData;
            return iconData;
        }
        else
        {
            IconInfoCache[iconID] = null;
            return null;
        }
    }

    public static long Time => DateTimeOffset.Now.ToUnixTimeMilliseconds();
    public static ulong Frame => Framework.Instance()->FrameCounter;

    public static LociSM GetManager(this Character chara, bool create = true)
        => LociManager.GetStatusManager(chara.GetNameWithWorld(), create);

    /// <summary>
    ///     Prepares to apply a LociStatus with the given preparation options. 
    /// </summary>
    public static LociStatus PreApply(this LociStatus status, params PrepareOptions[] opts)
    {
        status = status.NewtonsoftDeepClone();
        if (opts.Contains(PrepareOptions.ChangeGUID))
            status.GUID = Guid.NewGuid();
        // Update the persistent status.
        status.ExpiresAt = status.NoExpire ? long.MaxValue : Time + status.TotalMilliseconds;
        return status;
    }

    public static List<string> GetFriendlist()
    {
        var ret = new List<string>();
        var friends = (InfoProxyFriendList*)InfoModule.Instance()->GetInfoProxyById(InfoProxyId.FriendList);
        for (var i = 0; i < friends->InfoProxyCommonList.CharDataSpan.Length; i++)
        {
            var entry = friends->InfoProxyCommonList.CharDataSpan[i];
            var name = entry.NameString;
            if (name.Length is not 0)
                ret.Add($"{name}@{(GameDataSvc.WorldData.TryGetValue(entry.HomeWorld, out var world) ? world : "")}");
        }
        return ret;
    }

    /// <summary>
    ///     Returns a list of pointer addresses that are Character* references for the visible party members.
    /// </summary>
    public static List<nint> GetVisibleParty()
    {
        if (Svc.Party.Length < 2)
            return [PlayerData.Address];
        else
        {
            var ret = new List<nint>();
            // Get the specially ordered party members here.
            var hud = AgentHUD.Instance();
            var partyMembers = hud->PartyMembers.ToArray();
            var count = Math.Min((short)8, hud->PartyMemberCount);
            // Note the first person is always the player
            var sorted = partyMembers.OrderByDescending(m => (nint)m.Object != nint.Zero).ThenBy(m => m.Index).ToList();
            // Svc.Logger.Information($"Hud Members: {string.Join(", ", sorted.Select(m => $"{((nint)m.Object):X8} (idx {m.Index})"))}");
            // Sort them by the index that they appear in.
            for (var i = 0; i < Math.Min((short)8, hud->PartyMemberCount); i++)
            {
                if (sorted[i].Object is null || !sorted[i].Object->IsCharacter())
                {
                    ret.Add(nint.Zero);
                    continue;
                }
                // Add in the actor.
                ret.Add((nint)sorted[i].Object);
            }
            return ret;
        }
    }

    public static List<nint> GetNodeOrderedVisibleParty()
    {
        if (Svc.Party.Length < 2)
            return [PlayerData.Address];
        else
        {
            var ret = new List<nint>();
            // Get the specially ordered party members here.
            var hud = AgentHUD.Instance();
            var partyMembers = hud->PartyMembers.ToArray();
            var count = Math.Min((short)8, hud->PartyMemberCount);

            var sorted = partyMembers.Skip(1).OrderByDescending(m => (nint)m.Object != nint.Zero).ThenBy(m => m.Index).ToList();
            sorted.Insert(0, partyMembers[0]);
            // Svc.Logger.Information($"Hud Members: {string.Join(", ", sorted.Select(m => $"{((nint)m.Object):X8} (idx {m.Index})"))}");
            // Sort them by the index that they appear in.
            for (var i = 0; i < Math.Min((short)8, hud->PartyMemberCount); i++)
            {
                if (sorted[i].Object is null || !sorted[i].Object->IsCharacter())
                {
                    ret.Add(nint.Zero);
                    continue;
                }
                // Add in the actor.
                ret.Add((nint)sorted[i].Object);
            }
            return ret;
        }
    }

    public static bool CanSpawnVFX(this Character targetChara)
    {
        return true;
    }

    public static bool CanSpawnFlyText(this Character targetChara)
    {
        if (!targetChara.GetIsTargetable())
            return false;
        if (!PlayerData.Interactable)
            return false;
        if (Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Svc.Condition[ConditionFlag.WatchingCutscene]
            || Svc.Condition[ConditionFlag.WatchingCutscene78]
            || Svc.Condition[ConditionFlag.OccupiedInQuestEvent]
            || Svc.Condition[ConditionFlag.Occupied]
            || Svc.Condition[ConditionFlag.Occupied30]
            || Svc.Condition[ConditionFlag.Occupied33]
            || Svc.Condition[ConditionFlag.Occupied38]
            || Svc.Condition[ConditionFlag.Occupied39]
            || Svc.Condition[ConditionFlag.OccupiedInEvent]
            || Svc.Condition[ConditionFlag.BetweenAreas]
            || Svc.Condition[ConditionFlag.BetweenAreas51]
            || Svc.Condition[ConditionFlag.DutyRecorderPlayback]
            || Svc.Condition[ConditionFlag.LoggingOut])
            return false;
        return true;
    }


    public static SeString ParseBBSeString(string text, bool nullTerminator = true)
        => ParseBBSeString(text, out _, nullTerminator);
    
    public static SeString ParseBBSeString(string text, out bool hadError, bool nullTerminator = true)
    {
        hadError = false;
        try
        {
            var parts = SplitRegex().Split(text);
            var str = new SeStringBuilder();
            int[] openTags = new int[3]; // 0=color, 1=glow, 2=italics

            foreach (var s in parts)
            {
                if (s.Length is 0)
                    continue;

                if (TryParseColorTag(s, out var colorValue, out var isOpeningColor))
                {
                    if (isOpeningColor)
                    {
                        if (colorValue is 0 || Svc.Data.GetExcelSheet<UIColor>().GetRowOrDefault(colorValue) is null)
                            return ReturnError("Error: Color is out of range.", ref hadError);
                        str.AddUiForeground(colorValue);
                        openTags[0]++;
                    }
                    else
                    {
                        str.AddUiForegroundOff();
                        // Remove it, and error if it 0 prior to the removal.
                        if (openTags[0] <= 0)
                            return ReturnError("Error: Opening and closing color tags mismatch.", ref hadError);
                        openTags[0]--;
                    }
                    continue;
                }
                if (TryParseGlowTag(s, out var glowValue, out var isOpeningGlow))
                {
                    if (isOpeningGlow)
                    {
                        if (glowValue is 0 || Svc.Data.GetExcelSheet<UIColor>().GetRowOrDefault(glowValue) is null)
                            return ReturnError("Error: Glow color is out of range.", ref hadError);
                        // Add it, as it was successful.
                        str.AddUiGlow(glowValue);
                        openTags[1]++;
                    }
                    else
                    {
                        // Remove it, and error if it 0 prior to the removal.
                        str.AddUiGlowOff();
                        if (openTags[1] <= 0)
                            return ReturnError("Error: Opening and closing glow tags mismatch.", ref hadError);
                        openTags[1]--;
                    }
                    continue;
                }
                else if (s.Equals("[i]", StringComparison.OrdinalIgnoreCase))
                {
                    str.AddItalicsOn();
                    openTags[2]++;
                }
                else if (s.Equals("[/i]", StringComparison.OrdinalIgnoreCase))
                {
                    str.AddItalicsOff();
                    if (openTags[2] <= 0)
                        return ReturnError("Error: Opening and closing italics tags mismatch.", ref hadError);
                    openTags[2]--;
                }
                else
                {
                    str.AddText(s);
                }
            }

            // Fail if not all valid at the end
            if (!openTags.All(x => x == 0))
                return ReturnError("Error: Opening and closing elements mismatch.", ref hadError);

            if (nullTerminator)
                str.AddText("\0");

            hadError = false;
            return str.Build();
        }
        catch (Bagagwa ex)
        {
            hadError = true;
            return new SeStringBuilder().AddText($"{ex.Message}\0").Build();
        }

        SeString ReturnError(string errorMsg, ref bool hasError)
        {
            hasError = true;
            return new SeStringBuilder().AddText($"{errorMsg}\0").Build();
        }
    }

    // Helper to parse [color=xxx] and [/color]
    private static bool TryParseColorTag(string s, out ushort value, out bool isOpening)
    {
        value = 0;
        isOpening = false;
        if (s.StartsWith("[color=", StringComparison.OrdinalIgnoreCase))
        {
            isOpening = true;
            var content = s[7..^1];
            if (!ushort.TryParse(content, out value))
                value = (ushort)Enum.GetValues<XlDataUiColor>().FirstOrDefault(x => x.ToString().Equals(content, StringComparison.OrdinalIgnoreCase));
            return true;
        }
        if (s.Equals("[/color]", StringComparison.OrdinalIgnoreCase))
        {
            isOpening = false;
            return true;
        }

        return false;
    }

    // Helper to parse [glow=xxx] and [/glow]
    private static bool TryParseGlowTag(string s, out ushort value, out bool isOpening)
    {
        value = 0;
        isOpening = false;

        if (s.StartsWith("[glow=", StringComparison.OrdinalIgnoreCase))
        {
            isOpening = true;
            var content = s[6..^1];
            if (!ushort.TryParse(content, out value))
                value = (ushort)Enum.GetValues<XlDataUiColor>().FirstOrDefault(x => x.ToString().Equals(content, StringComparison.OrdinalIgnoreCase));
            return true;
        }
        if (s.Equals("[/glow]", StringComparison.OrdinalIgnoreCase))
        {
            isOpening = false;
            return true;
        }

        return false;
    }


    [GeneratedRegex(@"(\[color=[0-9a-zA-Z]+\])|(\[\/color\])|(\[glow=[0-9a-zA-Z]+\])|(\[\/glow\])|(\[i\])|(\[\/i\])", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex SplitRegex();

}

