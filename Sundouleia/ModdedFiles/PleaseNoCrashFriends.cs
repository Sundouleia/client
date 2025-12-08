using CkCommons;
using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation;
using FFXIVClientStructs.Havok.Common.Base.Types;
using FFXIVClientStructs.Havok.Common.Serialize.Util;
using Sundouleia.GameData;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Runtime.InteropServices;

namespace Sundouleia.ModFiles;

// Custom Skeletons love to crash people when sent over penumbra. This class aims to clean that up.
public class PlzNoCrashFrens
{
    private readonly ILogger<PlzNoCrashFrens> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly NoCrashFriendsConfig _config;
    private readonly FileCacheManager _manager;

    private readonly List<string> _failedMeshCalculations = [];

    public PlzNoCrashFrens(ILogger<PlzNoCrashFrens> logger, SundouleiaMediator mediator,
        NoCrashFriendsConfig config, FileCacheManager manager)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
        _manager = manager;
    }

    // Generously barrowed old Moon code for validating skeletons so that custom animations do not crash other peoples games.
    public unsafe Dictionary<string, List<ushort>>? GetSkeletonBoneIndices(GameObject* renderedObject)
    {
        if (renderedObject == null)
            return null;
        // Grab the character base from the game object model.
        var objectBase = (CharacterBase*)renderedObject->DrawObject;
        // if the model type is not human then we can ignore it as it is not important.
        if (objectBase->GetModelType() != CharacterBase.ModelType.Human)
            return null;
        // otherwise retrieve the skeleton resource handles for indices output monior.
        var skelHandles = objectBase->Skeleton->SkeletonResourceHandles;
        Dictionary<string, List<ushort>> outputIndices = [];
        Generic.Safe(() =>
        {
            // Scan over all partial skeletons for any custom bone detection.
            for (int i = 0; i < objectBase->Skeleton->PartialSkeletonCount; i++)
            {
                var handle = *(skelHandles + i);
                _logger.LogTrace($"Iterating over SkeletonResourceHandle #{i}:{((nint)handle).ToString("X")}", LoggerType.ResourceMonitor);
                if ((nint)handle == nint.Zero)
                    continue;

                var curBones = handle->BoneCount;
                // this is unrealistic, the filename shouldn't ever be that long
                if (handle->FileName.Length > 1024)
                    continue;

                var skeletonName = handle->FileName.ToString();
                if (string.IsNullOrEmpty(skeletonName))
                    continue;

                outputIndices[skeletonName] = new();
                for (ushort boneIdx = 0; boneIdx < curBones; boneIdx++)
                {
                    var boneName = handle->HavokSkeleton->Bones[boneIdx].Name.String;
                    if (boneName == null)
                        continue;

                    outputIndices[skeletonName].Add((ushort)(boneIdx + 1));
                }
            }
        });
        return (outputIndices.Count != 0 && outputIndices.Values.All(u => u.Count > 0)) ? outputIndices : null;
    }

    /// <summary>
    ///     This helps ensure we are not going to crash people! Yay! :D
    /// </summary>
    /// <returns> The list of hashes to remove from the persistent transients. (Occurs on validation failure) </returns>
    public async Task<List<string>> VerifyPlayerAnimationBones(Dictionary<string, List<ushort>>? boneIndices, HashSet<ModdedFile> moddedFiles, CancellationToken ct)
    {
        // If there was no indices then we do not care.
        if (boneIndices is null)
            return [];

        // Log the bone indices if found.
        foreach (var kvp in boneIndices)
            _logger.LogDebug($"Found {kvp.Key} ({(kvp.Value.Any() ? kvp.Value.Max() : 0)} bone indices) on player: {string.Join(',', kvp.Value)}", LoggerType.ResourceMonitor);

        if (boneIndices.All(u => u.Value.Count == 0))
            return [];

        int noValidationFailed = 0;
        // only scan animation files.
        var animationFiles = moddedFiles.Where(f => !f.IsFileSwap && f.GamePaths.First().EndsWith("pap", StringComparison.OrdinalIgnoreCase)).ToList();
        
        var toRemove = new List<string>();

        try
        {
            foreach (var file in animationFiles)
            {
                // throw if cancelled at any point in time.
                ct.ThrowIfCancellationRequested();

                // Grab the indices from the pap file, or the config cache if defined.
                var skeletonIndices = await Svc.Framework.RunOnFrameworkThread(() => GetBoneIndicesFromPap(file.Hash)).ConfigureAwait(false);
                // track if we failed validation.
                bool validationFailed = false;
                if (skeletonIndices != null)
                {
                    // 105 == Vanilla Player Skeleton maximum.
                    if (skeletonIndices.All(k => k.Value.Max() <= 105))
                    {
                        _logger.LogTrace($"All indices of {file.ResolvedPath} are <= 105, ignoring", LoggerType.ResourceMonitor);
                        continue;
                    }

                    _logger.LogDebug($"Verifying bone indices for {file.ResolvedPath}, found {skeletonIndices.Count} skeletons", LoggerType.ResourceMonitor);
                    // validate all skeletons against the player skeleton.
                    foreach (var boneCount in skeletonIndices.Select(k => k).ToList())
                    {
                        if (boneCount.Value.Max() > boneIndices.SelectMany(b => b.Value).Max())
                        {
                            _logger.LogWarning($"Found more bone indices on the animation {file.ResolvedPath} skeleton {boneCount.Key} (max indice {boneCount.Value.Max()})" +
                                $"than on any player related skeleton (max indice {boneIndices.SelectMany(b => b.Value).Max()})");
                            validationFailed = true;
                            // Break out of this foreach loop, we failed validation for this file.
                            break;
                        }
                    }
                }

                // if we failed, log it and remove it from the files that we send over.
                if (validationFailed)
                {
                    noValidationFailed++;
                    _logger.LogDebug($"Removing {file.ResolvedPath} from sent file replacements and transient data", LoggerType.ResourceMonitor);
                    moddedFiles.Remove(file);
                    toRemove.AddRange(file.GamePaths);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"Cancelled verifying player animation bones: {ex}");
            return [];
        }

        // If we failed any validation, notify the user that some of their animations were removed.
        if (noValidationFailed > 0)
        {
            _mediator.Publish(new NotificationMessage("Invalid Skeleton Setup",
                $"Tried sending {noValidationFailed} animations with bad skeletons, and were not sent." +
                $"Verify you're using the correct skeleton for those animations (Check /xllog for more information).",
                NotificationType.Warning, TimeSpan.FromSeconds(10)));
        }

        return toRemove;
    }

    /// <summary>
    ///     <c>{SkeletonName, List{ushort} BoneIndices}</c>, fetched by pap's file data-hash name. 
    /// </summary>
    public unsafe Dictionary<string, List<ushort>>? GetBoneIndicesFromPap(string hash)
    {
        // If we have already cached the bones from this file, then we can simply return the bones for the custom animation to avoid calculating this file.
        if (_config.Current.BonesDictionary.TryGetValue(hash, out var bones))
            return bones;

        // Attempt to retrieve the file from our cached file entries. If not found return and log.
        if (_manager.GetFileCacheByHash(hash) is not { } cacheEntity)
        {
            _logger.LogWarning($"Could not find file cache entry for hash: {hash}");
            return null;
        }

        // VFXEditor reads in streams via binary readers for edits. We can do the same to validate a path without modifying it.
        // If it ever changes at any point we will need to adjust this code.
        using BinaryReader reader = new BinaryReader(File.Open(cacheEntity.ResolvedFilepath, FileMode.Open, FileAccess.Read, FileShare.Read));

        reader.ReadInt32(); // ignore
        reader.ReadInt32(); // ignore
        reader.ReadInt16(); // read 2 (num animations)
        reader.ReadInt16(); // read 2 (modelid)
        var type = reader.ReadByte();// read 1 (type)

        // Type determines if human or not. If anything but 0, it is non-human so ignore it.
        if (type != 0) return null;

        reader.ReadByte(); // read 1 (variant)
        reader.ReadInt32(); // ignore
        // Determine havok skeleton positions and sizes.
        var havokPosition = reader.ReadInt32();
        var footerPosition = reader.ReadInt32();
        var havokDataSize = footerPosition - havokPosition;
        reader.BaseStream.Position = havokPosition;

        // If havok data size is less than 8 then there is no skeleton data to read.
        var havokData = reader.ReadBytes(havokDataSize);
        if (havokData.Length <= 8) 
            return null; 

        // Otherwise we can parse the information into temporary .hkx file formats to read in the data for analysis.
        var output = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        var tempHavokDataPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()) + ".hkx";
        var tempHavokDataPathAnsi = Marshal.StringToHGlobalAnsi(tempHavokDataPath);

        // The following is barrowed from VFXEditor:
        // https://github.com/0ceal0t/Dalamud-VFXEditor/blob/c320f08c981f3bf7353157c3d2faebcb00cba511/VFXEditor/Interop/Havok/HavokData.cs#L26
        try
        {
            File.WriteAllBytes(tempHavokDataPath, havokData);
            var loadoptions = stackalloc hkSerializeUtil.LoadOptions[1];
            loadoptions->TypeInfoRegistry = hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry();
            loadoptions->ClassNameRegistry = hkBuiltinTypeRegistry.Instance()->GetClassNameRegistry();
            loadoptions->Flags = new hkFlags<hkSerializeUtil.LoadOptionBits, int>
            {
                Storage = (int)(hkSerializeUtil.LoadOptionBits.Default)
            };

            var resource = hkSerializeUtil.LoadFromFile((byte*)tempHavokDataPathAnsi, null, loadoptions);
            if (resource is null)
                throw new InvalidOperationException("Resource was null after loading");

            var rootLevelName = @"hkRootLevelContainer"u8;
            fixed (byte* n1 = rootLevelName)
            {
                var container = (hkRootLevelContainer*)resource->GetContentsPointer(n1, hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry());
                var animationName = @"hkaAnimationContainer"u8;
                fixed (byte* n2 = animationName)
                {
                    var animContainer = (hkaAnimationContainer*)container->findObjectByName(n2, null);
                    for (int i = 0; i < animContainer->Bindings.Length; i++)
                    {
                        var binding = animContainer->Bindings[i].ptr;
                        var boneTransform = binding->TransformTrackToBoneIndices;
                        string name = binding->OriginalSkeletonName.String! + "_" + i;
                        output[name] = [];
                        for (int boneIdx = 0; boneIdx < boneTransform.Length; boneIdx++)
                        {
                            output[name].Add((ushort)boneTransform[boneIdx]);
                        }
                        output[name].Sort();
                    }

                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load havok file in {path}", tempHavokDataPath);
        }
        finally
        {
            // Free any allocated memory and remove the temporary havok data file.
            Marshal.FreeHGlobal(tempHavokDataPathAnsi);
            File.Delete(tempHavokDataPath);
        }

        // Store the parsed output information into the config storage.
        _config.Current.BonesDictionary[hash] = output;
        _config.Save();
        return output;
    }

    // Maybe shift back to a Task<long> if we need to configure await to run off the UI thread.
    public long GetTrianglesByHash(string hash)
    {
        if (_config.Current.ModelTris.TryGetValue(hash, out var cachedTris) && cachedTris > 0)
            return cachedTris;

        if (_failedMeshCalculations.Contains(hash, StringComparer.Ordinal))
            return 0;

        var path = _manager.GetFileCacheByHash(hash);
        if (path is null || !path.ResolvedFilepath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            return 0;

        var filePath = path.ResolvedFilepath;

        try
        {
            _logger.LogDebug($"Detected Model File {filePath}. Calculating Tris");
            var file = new MdlFile(filePath);
            // Models without a LOD failed to construct.
            if (file.LodCount <= 0)
            {
                _failedMeshCalculations.Add(hash);
                _config.Current.ModelTris[hash] = 0;
                _config.Save();
                return 0;
            }
            // Scan over LODs until we find a valid triangle count.
            long tris = 0;
            for (int i = 0; i < file.LodCount; i++)
            {
                try
                {
                    var meshIdx = file.Lods[i].MeshIndex;
                    var meshCnt = file.Lods[i].MeshCount;
                    tris = file.Meshes.Skip(meshIdx).Take(meshCnt).Sum(p => p.IndexCount) / 3;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"LOD Mesh {i} from [{filePath}] failed to load: ({ex})");
                    continue;
                }

                // Once we found a valid tri count store it and break.
                if (tris > 0)
                {
                    _logger.LogDebug("TriAnalysis: {filePath} => {tris} triangles", filePath, tris);
                    _config.Current.ModelTris[hash] = tris;
                    _config.Save();
                    break;
                }
            }
            // Ret the correctly calculated tri's for this mdl file.
            return tris;
        }
        catch (Exception ex)
        {
            _failedMeshCalculations.Add(hash);
            _config.Current.ModelTris[hash] = 0;
            _config.Save();
            _logger.LogWarning($"Failed to parse: {filePath} ({ex})");
            return 0;
        }
    }
}