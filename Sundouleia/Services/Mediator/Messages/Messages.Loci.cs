using Sundouleia.Loci.Data;

namespace Sundouleia.Services.Mediator;

// Ensure samethread, as this is called frequently.
// (This is true for all methods)
//public record LociSMModified(IntPtr Address) : SameThreadMessage;
//public record LociStatusModified(LociStatus Status, bool Deleted) : SameThreadMessage;
//public record LociPresetModified(LociPreset Preset, bool Deleted) : SameThreadMessage;
//public record LociApplyToTarget(IntPtr Target, string TargetHost, LociStatusInfo Data) : SameThreadMessage;
//public record LociApplyToTargetBulk(IntPtr Target, string TargetHost, List<LociStatusInfo> Data) : SameThreadMessage;

// The above is temporarily running on raw event calls to allow static calls to not place extra stress on Sundouleia Services.