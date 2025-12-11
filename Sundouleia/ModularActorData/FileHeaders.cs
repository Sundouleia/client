using System.Security.Cryptography;
using System.Text.Json;

namespace Sundouleia.ModularActorData;

// Header record that could be transformed into some class or something idk.
internal record SmadHeader(
    byte Version, 
    Guid FileId, 
    byte[] Salt, 
    byte[] KeyHash, 
    byte[] OwnerPubKey,               // SPKI bytes
    string[] AllowedHashes,
    SignedHeaderSignature? OwnerHeaderSignature // signature over header fields by owner
);

internal record SignedHeaderSignature(byte[] Signature);

internal record UpdateTokenPayload(Guid FileId, string[] AddHashes, string[] RemoveHashes, long ExpiresUtcTicks, string Nonce);

// OwnerSignature = Sign(payload)
internal record UpdateToken(UpdateTokenPayload Payload, byte[] OwnerSignature);