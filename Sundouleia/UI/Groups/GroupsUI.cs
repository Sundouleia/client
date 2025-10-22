using CkCommons;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class GroupsUI : WindowMediatorSubscriberBase
{
    private readonly GroupsSelector _selector;
    private readonly TutorialService _guides;
    public GroupsUI(ILogger<GroupsUI> logger, SundouleiaMediator mediator, GroupsSelector selector,
        TutorialService guides) : base(logger, mediator, "Group Manager")
    {
        _selector = selector;
        _guides = guides;

        this.PinningClickthroughFalse();
        this.SetBoundaries(new(550, 470), ImGui.GetIO().DisplaySize);        
        TitleBarButtons = new TitleBarButtonBuilder()
            .AddTutorial(guides, TutorialType.Groups)
            .Build();
    }

    private bool ThemePushed = false;
    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {

    }
}
