using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Security.Cryptography;

namespace Sundouleia.WebAPI.Utils;

public static class SundouleiaSecurity
{
    // i think they are just using the player hash to make it more less likely to have the same identifier occur? If so, all of this is useless.
    private static readonly Dictionary<(string, ushort), string> _hashListPlayersSHA256 = new();
    private static readonly Dictionary<string, string> _hashListSHA256 = new(StringComparer.Ordinal);
    private static readonly SHA256 _sha256CryptoProvider = SHA256.Create();

    public static string GetFileHash(this string filePath)
    {
        using var cryptoProvider = SHA256.Create();
        return BitConverter.ToString(cryptoProvider.ComputeHash(File.ReadAllBytes(filePath))).Replace("-", "", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Only call this when the ptr is visible.
    /// </summary>
    public unsafe static string GetIdentHashByCharacterPtr(nint address)
        => ((Character*)address)->ContentId.ToString().GetHash256();

    /// <summary>
    ///     Only call this when you are visible.
    /// </summary>
    public static async Task<string> GetClientIdentHash()
        => await Svc.Framework.RunOnFrameworkThread(() => Svc.ClientState.LocalContentId.ToString().GetHash256());

    // dont even ask me what any of these do, its just black magic encryption bullshit.
    public static string GetHash256(this (string, ushort) playerToHash)
    {
        if (_hashListPlayersSHA256.TryGetValue(playerToHash, out var hash))
            return hash;

        return _hashListPlayersSHA256[playerToHash] =
            BitConverter.ToString(_sha256CryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(playerToHash.Item1 + playerToHash.Item2.ToString()))).Replace("-", "", StringComparison.Ordinal);
    }

    // for right now i wont be encrypting in and out the secret keys but later on i can keep this here and just add it back in. 
    public static string GetHash256(this string stringToHash)
        => GetOrComputeHashSHA256(stringToHash);

    private static string GetOrComputeHashSHA256(string stringToCompute)
    {
        if (_hashListSHA256.TryGetValue(stringToCompute, out var hash))
            return hash;

        return _hashListSHA256[stringToCompute] =
            BitConverter.ToString(_sha256CryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(stringToCompute))).Replace("-", "", StringComparison.Ordinal);
    }
}

