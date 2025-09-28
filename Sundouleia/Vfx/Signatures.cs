namespace Sundouleia.GameInternals;

// No references included here. If you are poking around here you should know what you are doing.
public static class Signatures
{
    // ScanType: Signature
    public const string OnEmote = "E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? 4C 89 74 24";

    // DetourName = nameof(ApplyGlamourPlateDetour), Fallibility = Fallibility.Auto, Define via SignatureAttribute.
    public const string ApplyGlamourPlate = "E8 ?? ?? ?? ?? 41 C6 44 24 ?? ?? E9 ?? ?? ?? ?? 0F B6 83";

    // DetourName = nameof(ProcessChatInputDetour), Fallibility = Fallibility.Auto, Define via SignatureAttribute.
    public const string ProcessChatInput = "E8 ?? ?? ?? ?? FE 87 ?? ?? ?? ?? C7 87";

    // Spatial Audio Sigs from VFXEDITOR
    internal const string CreateStaticVfx = "E8 ?? ?? ?? ?? F3 0F 10 35 ?? ?? ?? ?? 48 89 43 08";
    internal const string RunStaticVfx = "E8 ?? ?? ?? ?? ?? ?? ?? 8B 4A ?? 85 C9";
    internal const string RemoveStaticVfx = "40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9";

    internal const string CreateActorVfx = "40 53 55 56 57 48 81 EC ?? ?? ?? ?? 0F 29 B4 24 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 AC 24 ?? ?? ?? ?? 0F 28 F3 49 8B F8";
    internal const string RemoveActorVfx = "0F 11 48 10 48 8D 05"; // the weird one

    // CORBY'S BLACK MAGIC SIGs
    // sub_1417229C0(nint a1, nint a2)
    public const string UnkAutoMoveUpdate = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 20 44 0F B6 7A ?? 48 8B D9";
}
