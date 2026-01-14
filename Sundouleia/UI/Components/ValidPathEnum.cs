using CkCommons;
using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using OtterGui.Text;

namespace Sundouleia.Gui;

public enum FilePathValidation
{
    Valid = 0 << 0,
    NotWritable = 1 << 0,
    IsOneDrive = 1 << 1,
    IsPenumbraDir = 1 << 2,
    HasOtherFiles = 1 << 3,
    InvalidPath = 1 << 4,
}

//private void DrawAddressConfig(KinksterInfoCache cache, Kinkster k, string dispName, float width)
//{
//    using var c = CkRaii.FramedChildPaddedWH("##AddressConfig", new(width, CkStyle.GetFrameRowsHeight(3).AddWinPadY()), 0, CkColor.VibrantPink.Uint());

//    CkGui.FramedIconText(FAI.Home);
//    ImUtf8.SameLineInner();
//    var widthMinusFrame = ImGui.GetContentRegionAvail().X;
//    var triItemWidth = (widthMinusFrame - ImGui.GetStyle().ItemInnerSpacing.X * 2) / 3f;

//    // over the next line, have 3 buttons for the various property type states.
//    using (ImRaii.Disabled(cache.Address.PropertyType is PropertyType.House))
//        if (ImGui.Button("House", new Vector2(triItemWidth, ImGui.GetFrameHeight())))
//        {
//            cache.Address.PropertyType = PropertyType.House;
//            cache.Address.ApartmentSubdivision = false;
//        }
//    CkGui.AttachToolTip($"Confining {dispName} in a house.");

//    ImUtf8.SameLineInner();
//    using (ImRaii.Disabled(cache.Address.PropertyType is PropertyType.Apartment))
//        if (ImGui.Button("Apartment", new Vector2(triItemWidth, ImGui.GetFrameHeight())))
//            cache.Address.PropertyType = PropertyType.Apartment;
//    CkGui.AttachToolTip($"Confining {dispName} to an apartment room.");

//    ImUtf8.SameLineInner();
//    using (ImRaii.Disabled(cache.Address.PropertyType is PropertyType.PrivateChambers))
//        if (ImGui.Button("Chambers", new Vector2(triItemWidth, ImGui.GetFrameHeight())))
//        {
//            cache.Address.PropertyType = PropertyType.PrivateChambers;
//            cache.Address.ApartmentSubdivision = false;
//        }
//    CkGui.AttachToolTip($"Confining {dispName} the private chambers of an FC.");

//    CkGui.FramedIconText(FAI.MapMarkedAlt);
//    ImUtf8.SameLineInner();

//    if (cache.Worlds.Draw(cache.Address.World, triItemWidth, CFlags.NoArrowButton))
//        cache.Address.World = cache.Worlds.Current.Key.Id;
//    CkGui.AttachToolTip($"The World {dispName} will be confined to.");

//    ImUtf8.SameLineInner();
//    CkGuiUtils.ResidentialAetheryteCombo($"##resdis", triItemWidth, ref cache.Address.City);
//    CkGui.AttachToolTip($"The District {dispName} will be confined to.");

//    ImUtf8.SameLineInner();
//    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
//    ImGui.DragInt($"##ward", ref cache.Address.Ward, .5f, 1, 30, "Ward %d");
//    CkGui.AttachToolTip($"The Ward {dispName} will be confined to.");

//    var propType = cache.Address.PropertyType;
//    using (ImRaii.Disabled(propType is not PropertyType.Apartment))
//        ImGui.Checkbox("##SubdivisionCheck", ref cache.Address.ApartmentSubdivision);
//    CkGui.AttachToolTip("If the apartment is in the ward's subdivision.");

//    // draw out the sliders based on the property type.
//    ImUtf8.SameLineInner();
//    var sliderW = propType is not PropertyType.PrivateChambers
//        ? ImGui.GetContentRegionAvail().X : (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemInnerSpacing.X) / 2;
//    switch (propType)
//    {
//        case PropertyType.House:
//            ImGui.SetNextItemWidth(sliderW);
//            ImGui.SliderInt("##plot", ref cache.Address.Plot, 1, 60, "Plot %d");
//            CkGui.AttachToolTip($"The plot # of the home {dispName} will be confined to.");
//            break;
//        case PropertyType.Apartment:
//            ImGui.SetNextItemWidth(sliderW);
//            ImGui.SliderInt("##room", ref cache.Address.Apartment, 1, 100, "Room %d");
//            CkGui.AttachToolTip($"The apartment room # {dispName} will be confined to.");
//            break;
//        case PropertyType.PrivateChambers:
//            ImGui.SetNextItemWidth(sliderW);
//            ImGui.SliderInt("##plot", ref cache.Address.Plot, 1, 60, "Plot %d");
//            CkGui.AttachToolTip($"The plot # of the home {dispName} will be confined to.");

//            ImUtf8.SameLineInner();
//            ImGui.SetNextItemWidth(sliderW);
//            ImGui.SliderInt("##chambers", ref cache.Address.Apartment, 1, 100, "Chamber %d");
//            CkGui.AttachToolTip($"The private chambers # {dispName} will be confined to.");
//            break;
//        default:
//            CkGui.ColorTextCentered("UNKNOWN PLOT TYPE", ImGuiColors.DalamudRed);
//            break;
//    }
//}