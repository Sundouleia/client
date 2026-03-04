using CkCommons.DrawSystem;
using Dalamud.Bindings.ImGui;
using Sundouleia.Radar;

namespace Sundouleia.DrawSystem;

public sealed class RadarFolder : DynamicFolder<RadarUser>
{
    private Func<IReadOnlyList<RadarUser>> _generator;
    public RadarFolder(DynamicFolderGroup<RadarUser> parent, uint id, FAI icon, string name,
        Func<IReadOnlyList<RadarUser>> gen)
        : base(parent, icon, name, id)
    {
        // Can set stylizations here.
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        GradientColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = gen;
    }

    public RadarFolder(DynamicFolderGroup<RadarUser> parent, uint id, FAI icon, string name,
        Func<IReadOnlyList<RadarUser>> generator, IReadOnlyList<ISortMethod<DynamicLeaf<RadarUser>>> sortSteps)
        : base(parent, icon, name, id, new(sortSteps))
    {
        // Can set stylizations here.
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        GradientColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    public int Rendered => Children.Count(s => s.Data.IsValid);
    public int Lurkers => Children.Count(s => !s.Data.IsValid);
    protected override IReadOnlyList<RadarUser> GetAllItems() => _generator();
    protected override DynamicLeaf<RadarUser> ToLeaf(RadarUser item) => new(this, item.UID, item);
}