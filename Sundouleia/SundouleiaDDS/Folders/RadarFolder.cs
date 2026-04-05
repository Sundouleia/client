using CkCommons.DrawSystem;
using Dalamud.Bindings.ImGui;
using Sundouleia.Radar;

namespace Sundouleia.DrawSystem;

public sealed class RadarFolder : DynamicFolder<RadarPublicUser>
{
    private Func<IReadOnlyList<RadarPublicUser>> _generator;
    public RadarFolder(DynamicFolderGroup<RadarPublicUser> parent, uint id, FAI icon, string name,
        Func<IReadOnlyList<RadarPublicUser>> gen)
        : base(parent, icon, name, id)
    {
        // Can set stylizations here.
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        GradientColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = gen;
    }

    public RadarFolder(DynamicFolderGroup<RadarPublicUser> parent, uint id, FAI icon, string name,
        Func<IReadOnlyList<RadarPublicUser>> generator, IReadOnlyList<ISortMethod<DynamicLeaf<RadarPublicUser>>> sortSteps)
        : base(parent, icon, name, id, new(sortSteps))
    {
        // Can set stylizations here.
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        GradientColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    public int Rendered => Children.Count(s => s.Data.IsValid);
    public int Lurkers => Children.Count(s => !s.Data.IsValid);
    protected override IReadOnlyList<RadarPublicUser> GetAllItems() => _generator();
    protected override DynamicLeaf<RadarPublicUser> ToLeaf(RadarPublicUser item) => new(this, item.UID, item);
}