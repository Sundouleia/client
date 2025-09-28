using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using Sundouleia.Services.Events;
using Sundouleia.Services.Mediator;
using System.Globalization;
using OtterGui.Text;
using OtterGui;
using Sundouleia.Services.Configs;
using CkCommons.Gui;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

internal class DataEventsUI : WindowMediatorSubscriberBase
{
    private readonly EventAggregator _eventAggregator;
    private bool ThemePushed = false;

    private List<DataEvent> CurrentEvents => _eventAggregator.EventList.Value.OrderByDescending(f => f.EventTime).ToList();
    private List<DataEvent> FilteredEvents => CurrentEvents.Where(f => (string.IsNullOrEmpty(FilterText) || ApplyDynamicFilter(f))).ToList();
    private string FilterText = string.Empty;
    private InteractionFilter FilterCategory = InteractionFilter.All;

    public DataEventsUI(ILogger<DataEventsUI> logger, SundouleiaMediator mediator, EventAggregator events) 
        : base(logger, mediator, "Interaction Events Viewer")
    {
        _eventAggregator = events;

        Flags = WFlags.NoScrollbar | WFlags.NoCollapse;
        this.PinningClickthroughFalse();
        this.SetBoundaries(new(500, 300), new(600, 2000));
    }

    private bool ApplyDynamicFilter(DataEvent f)
    {
        // Map each InteractionFilter type to the corresponding property check
        var filterMap = new Dictionary<InteractionFilter, Func<DataEvent, string>>
        {
            { InteractionFilter.Applier, e => $"{e.NickAliasOrUID} {e.UserUID}" },
            { InteractionFilter.Interaction, e => e.Type.ToString() },
            { InteractionFilter.Content, e => e.DataSummary }
        };

        // If "All" is selected, return true if any of the fields contain the filter text
        if (FilterCategory is InteractionFilter.All)
            return filterMap.Values.Any(getField => getField(f).Contains(FilterText, StringComparison.OrdinalIgnoreCase));

        // Otherwise, use the selected filter type to apply the filter
        return filterMap.TryGetValue(FilterCategory, out var getField)
            && getField(f).Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }


    private void ClearFilters()
    {
        FilterText = string.Empty;
        FilterCategory = InteractionFilter.All;
    }

    public override void OnOpen()
        => ClearFilters();

    protected override void PreDrawInternal()
    {
        if (!ThemePushed)
        {
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.331f, 0.081f, 0.169f, .803f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.579f, 0.170f, 0.359f, 0.828f));
            ThemePushed = true;
        }
    }

    protected override void PostDrawInternal()
    {
        if (ThemePushed)
        {
            ImGui.PopStyleColor(2);
            ThemePushed = false;
        }
    }
    protected override void DrawInternal()
    {
        using (ImRaii.Group())
        {
            // Draw out the clear filters button
            if (CkGui.IconTextButton(FAI.Ban, "Clear"))
                ClearFilters();

            // On the same line, draw out the search bar.
            ImUtf8.SameLineInner();
            ImGui.SetNextItemWidth(160f);
            ImGui.InputTextWithHint("##DataEventSearch", "Search Filter Text...", ref FilterText, 64);

            // On the same line, draw out the filter category dropdown
            ImUtf8.SameLineInner();
            if(ImGuiUtil.GenericEnumCombo("##EventFilterType", 110f, FilterCategory, out InteractionFilter newValue, i => i.ToName()))
                FilterCategory = newValue;


            // On the same line, at the very end, draw the button to open the event folder.
            var buttonSize = CkGui.IconTextButtonSize(FAI.FolderOpen, "EventLogs");
            var distance = ImGui.GetContentRegionAvail().X - buttonSize;
            ImGui.SameLine(distance);
            if (CkGui.IconTextButton(FAI.FolderOpen, "EventLogs"))
            {
                ProcessStartInfo ps = new()
                {
                    FileName = ConfigFileProvider.EventDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                Process.Start(ps);
            }
        }

        DrawInteractionsList();
    }

    private void DrawInteractionsList()
    {
        var cursorPos = ImGui.GetCursorPosY();
        var max = ImGui.GetWindowContentRegionMax();
        var min = ImGui.GetWindowContentRegionMin();
        var width = max.X - min.X;
        var height = max.Y - cursorPos;
        using var table = ImRaii.Table("interactionsTable", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg, new Vector2(width, height));
        if (!table)
            return;

        ImGui.TableSetupColumn("Time");
        ImGui.TableSetupColumn("Applier");
        ImGui.TableSetupColumn("Interaction");
        ImGui.TableSetupColumn("Details");
        ImGui.TableHeadersRow();

        foreach (var ev in FilteredEvents)
        {
            ImGui.TableNextColumn();
            // Draw out the time it was applied
            CkGui.TextFrameAligned(ev.EventTime.ToString("T", CultureInfo.CurrentCulture));
            ImGui.TableNextColumn();
            // Draw out the applier
            CkGui.TextFrameAligned(!string.IsNullOrEmpty(ev.NickAliasOrUID) ? ev.NickAliasOrUID : (!string.IsNullOrEmpty(ev.UserUID) ? ev.UserUID : "--")); 
            ImGui.TableNextColumn();
            // Draw out the interaction type
            CkGui.TextFrameAligned(ev.Type.ToName());
            ImGui.TableNextColumn();
            // Draw out the details
            ImGui.AlignTextToFramePadding();
            var posX = ImGui.GetCursorPosX();
            var maxTextLength = ImGui.GetWindowContentRegionMax().X - posX;
            var textSize = ImGui.CalcTextSize(ev.DataSummary).X;
            var msg = ev.DataSummary;
            while (textSize > maxTextLength)
            {
                msg = msg[..^5] + "...";
                textSize = ImGui.CalcTextSize(msg).X;
            }
            ImGui.TextUnformatted(msg);
            if (!string.Equals(msg, ev.DataSummary, StringComparison.Ordinal))
                CkGui.AttachToolTip(ev.DataSummary);
        }
    }
}
