using K4os.Compression.LZ4.Legacy;
using Lumina.Data.Parsing.Scd;
using Penumbra.String.Classes;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Watchers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModularActorData;

// Represents a modded file entry.
public record FileModData(IEnumerable<string> GamePaths, int Length, string FileHash);

// Represents a vanilla file swap entry.
public record FileSwap(IEnumerable<string> GamePaths, string FileSwapPath);
