using Dalamud.Bindings.ImGui;
using Sundouleia.PlayerClient;

namespace Sundouleia.DrawSystem;

// All folders are a descendant of root.
public sealed class RequestFolder : DynamicFolder<RequestEntry>
{
    private Func<IReadOnlyList<RequestEntry>> _generator;
    public RequestFolder(DynamicFolderGroup<RequestEntry> parent, uint id, FAI icon, string name, 
        Func<IReadOnlyList<RequestEntry>> gen)
        : base(parent, icon, name, id, null, FolderFlags.Expanded)
    {
        // Can set stylizations here.
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = gen;
    }

    public RequestFolder(DynamicFolderGroup<RequestEntry> parent, uint id, FAI icon, string name,
        Func<IReadOnlyList<RequestEntry>> generator, IReadOnlyList<ISortMethod<DynamicLeaf<RequestEntry>>> sortSteps)
        : base(parent, icon, name, id, new(sortSteps), FolderFlags.Expanded)
    {
        // Can set stylizations here.
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    protected override IReadOnlyList<RequestEntry> GetAllItems() => _generator();
    protected override DynamicLeaf<RequestEntry> ToLeaf(RequestEntry item) 
        => new(this, item.FromClient ? item.RecipientUID : item.SenderUID, item);
}