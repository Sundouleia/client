using Dalamud.Interface.Colors;
using Sundouleia.Localization;
using Dalamud.Bindings.ImGui;
using System.Runtime.CompilerServices;

// A Modified take on OtterGui.Widgets.Tutorial.
// This iteration removes redundant buttons, adds detailed text, and sections.
namespace Sundouleia.Services.Tutorial;

public class TutorialService
{
    private readonly Dictionary<TutorialType, Tutorial> _tutorials = new();

    public TutorialService() { }
    public bool IsTutorialActive(TutorialType type) => _tutorials[type].CurrentStep is not -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void StartTutorial(TutorialType guide)
    {
        if (!_tutorials.ContainsKey(guide))
            return;

        // set all other tutorials to -1, stopping them.
        foreach (var t in _tutorials)
            t.Value.CurrentStep = (t.Key != guide) ?  -1 : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OpenTutorial<TEnum>(TutorialType guide, TEnum step, Vector2 pos, Vector2 size, Action? onNext = null) where TEnum : Enum
    {
        if (_tutorials.TryGetValue(guide, out var tutorial))
            tutorial.Open(Convert.ToInt32(step), pos, size, onNext);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipTutorial(TutorialType guide)
    {
        // reset the step to -1, stopping the tutorial.
        if (_tutorials.TryGetValue(guide, out var tutorial))
            tutorial.CurrentStep = -1;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void JumpToStep<TEnum>(TutorialType guide, TEnum step)
    {
        // reset the step to -1, stopping the tutorial.
        if (_tutorials.TryGetValue(guide, out var tutorial))
            tutorial.CurrentStep = Convert.ToInt32(step);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CurrentStep(TutorialType guide)
    {
        if (_tutorials.TryGetValue(guide, out var tutorial))
            return tutorial.CurrentStep;

        return -1;
    }

    // Create a mappinng between the tutorialTypes and the associated enum size.
    private static readonly Dictionary<TutorialType, int> _tutorialSizes = new()
    {
        { TutorialType.MainUi, Enum.GetValues<StepsMainUi>().Length },
        { TutorialType.Groups, Enum.GetValues<StepsGroups>().Length },
    };

    public void InitializeTutorialStrings()
    {
        var mainUiStr = CkLoc.Tutorials.MainUi;
        _tutorials[TutorialType.MainUi] = new Tutorial()
        {
            BorderColor = ImGui.GetColorU32(ImGuiColors.TankBlue),
            HighlightColor = ImGui.GetColorU32(ImGuiColors.TankBlue),
            PopupLabel = "Main UI Tutorial",
        }
        .AddStep(mainUiStr.Step1Title, mainUiStr.Step1Desc, mainUiStr.Step1DescExtended)
        .AddStep(mainUiStr.Step2Title, mainUiStr.Step2Desc, mainUiStr.Step2DescExtended)
        .AddStep(mainUiStr.Step3Title, mainUiStr.Step3Desc)
        .AddStep(mainUiStr.Step4Title, mainUiStr.Step4Desc, mainUiStr.Step4DescExtended)
        .AddStep(mainUiStr.Step5Title, mainUiStr.Step5Desc, mainUiStr.Step5DescExtended)
        .AddStep(mainUiStr.Step6Title, mainUiStr.Step6Desc, mainUiStr.Step6DescExtended)
        .AddStep(mainUiStr.Step7Title, mainUiStr.Step7Desc, mainUiStr.Step7DescExtended)
        .AddStep(mainUiStr.Step8Title, mainUiStr.Step8Desc)
        .AddStep(mainUiStr.Step9Title, mainUiStr.Step9Desc, mainUiStr.Step9DescExtended)
        .AddStep(mainUiStr.Step10Title, mainUiStr.Step10Desc)
        .AddStep(mainUiStr.Step11Title, mainUiStr.Step11Desc, mainUiStr.Step11DescExtended)
        .AddStep(mainUiStr.Step12Title, mainUiStr.Step12Desc)
        .AddStep(mainUiStr.Step13Title, mainUiStr.Step13Desc, mainUiStr.Step13DescExtended)
        .AddStep(mainUiStr.Step14Title, mainUiStr.Step14Desc, mainUiStr.Step14DescExtended)
        .AddStep(mainUiStr.Step15Title, mainUiStr.Step15Desc, mainUiStr.Step15DescExtended)
        .AddStep(mainUiStr.Step16Title, mainUiStr.Step16Desc, mainUiStr.Step16DescExtended)
        .AddStep(mainUiStr.Step17Title, mainUiStr.Step17Desc, mainUiStr.Step17DescExtended)
        .AddStep(mainUiStr.Step18Title, mainUiStr.Step18Desc, mainUiStr.Step18DescExtended)
        .AddStep(mainUiStr.Step19Title, mainUiStr.Step19Desc, mainUiStr.Step19DescExtended)
        .AddStep(mainUiStr.Step20Title, mainUiStr.Step20Desc)
        .AddStep(mainUiStr.Step21Title, mainUiStr.Step21Desc, mainUiStr.Step21DescExtended)
        .AddStep(mainUiStr.Step22Title, mainUiStr.Step22Desc, mainUiStr.Step22DescExtended)
        .AddStep(mainUiStr.Step23Title, mainUiStr.Step23Desc)
        .AddStep(mainUiStr.Step24Title, mainUiStr.Step24Desc, mainUiStr.Step24DescExtended)
        .AddStep(mainUiStr.Step25Title, mainUiStr.Step25Desc)
        .AddStep(mainUiStr.Step26Title, mainUiStr.Step26Desc)
        .EnsureSize(_tutorialSizes[TutorialType.MainUi]);

        var remoteStr = CkLoc.Tutorials.Groups;
        _tutorials[TutorialType.Groups] = new Tutorial()
        {
            BorderColor = ImGui.GetColorU32(ImGuiColors.TankBlue),
            HighlightColor = ImGui.GetColorU32(ImGuiColors.TankBlue),
            PopupLabel = "Groups Tutorial",
        }
        .AddStep(remoteStr.Step1Title, remoteStr.Step1Desc)
        .EnsureSize(_tutorialSizes[TutorialType.Groups]);
    }
}
