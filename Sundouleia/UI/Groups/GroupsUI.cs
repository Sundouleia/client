using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class GroupsUI : WindowMediatorSubscriberBase
{
    private readonly GroupsSelector _selector;
    private readonly IpcCallerPenumbra _ipc;
    private readonly TutorialService _guides;
    public GroupsUI(ILogger<GroupsUI> logger, SundouleiaMediator mediator, GroupsSelector selector,
        IpcCallerPenumbra ipc, TutorialService guides) 
        : base(logger, mediator, "Group Manager###Sundouleia_GroupUI")
    {
        _selector = selector;
        _ipc = ipc;
        _guides = guides;

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(550, 470), ImGui.GetIO().DisplaySize);        
        TitleBarButtons = new TitleBarButtonBuilder()
            .AddTutorial(guides, TutorialType.Groups)
            .Build();
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
    }
}
