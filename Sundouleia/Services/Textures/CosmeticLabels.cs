namespace Sundouleia.Services.Textures;

public static class CosmeticLabels
{
    public static readonly Dictionary<EmoteTexture, string> EmoteTextures = new()
    {
        { EmoteTexture.Ashamed, "Emotes\\ashamed.png" },
        { EmoteTexture.Blush, "Emotes\\blush.png" },
        { EmoteTexture.Bow, "Emotes\\bow.png" },
        { EmoteTexture.Burrow, "Emotes\\burrow.png" },
        { EmoteTexture.CatPat, "Emotes\\catpat.png" },
        { EmoteTexture.Cheer, "Emotes\\cheer.png" },
        { EmoteTexture.Cute, "Emotes\\cute.png" },
        { EmoteTexture.Doze, "Emotes\\doze.png" },
        { EmoteTexture.Eager, "Emotes\\eager.png" },
        { EmoteTexture.Fight, "Emotes\\fight.png" },
        { EmoteTexture.Goodnight, "Emotes\\goodnight.png" },
        { EmoteTexture.Happy, "Emotes\\happy.png" },
        { EmoteTexture.Hi, "Emotes\\hi.png" },
        { EmoteTexture.Hmph, "Emotes\\hmph.png" },
        { EmoteTexture.Huh, "Emotes\\huh.png" },
        { EmoteTexture.Kiss, "Emotes\\kiss.png" },
        { EmoteTexture.Laugh, "Emotes\\laugh.png" },
        { EmoteTexture.Morning, "Emotes\\morning.png" },
        { EmoteTexture.Needy, "Emotes\\needy.png" },
        { EmoteTexture.Ok, "Emotes\\ok.png" },
        { EmoteTexture.Peek, "Emotes\\peek.png" },
        { EmoteTexture.Pout, "Emotes\\pout.png" },
        { EmoteTexture.Sweetie, "Emotes\\sweetie.png" },
        { EmoteTexture.Wah, "Emotes\\wah.png" },
        { EmoteTexture.Wow, "Emotes\\wow.png" },
        { EmoteTexture.Yeah, "Emotes\\yeah.png" },
    };

    public static readonly Dictionary<CoreTexture, string> NecessaryImages = new()
    {
        { CoreTexture.Achievement, "RequiredImages\\achievement.png" },
        { CoreTexture.AchievementLineSplit, "RequiredImages\\achievementlinesplit.png" },
        { CoreTexture.Clock, "RequiredImages\\clock.png" },
        { CoreTexture.Edit, "RequiredImages\\edit.png" },
        { CoreTexture.Icon256, "RequiredImages\\icon256.png" },
        { CoreTexture.Icon256Bg, "RequiredImages\\icon256bg.png" },
        { CoreTexture.Radar, "RequiredImages\\radar.png" },
        { CoreTexture.ReportBg, "RequiredImages\\reportBg.png" },
        { CoreTexture.ReportBorder , "RequiredImages\\reportBorder.png" },
        { CoreTexture.Tier1Icon, "RequiredImages\\Tier1Icon.png" },
        { CoreTexture.Tier2Icon, "RequiredImages\\Tier2Icon.png" },
        { CoreTexture.Tier3Icon, "RequiredImages\\Tier3Icon.png" },
        { CoreTexture.Tier4Icon, "RequiredImages\\Tier4Icon.png" },
        { CoreTexture.TierBoosterIcon, "RequiredImages\\TierBoosterIcon.png" },
        { CoreTexture.WelcomeOverlay, "RequiredImages\\welcomeOverlay.png" },
    };

    public static readonly Dictionary<string, string> CosmeticTextures = InitializeCosmeticTextures();

    private static Dictionary<string, string> InitializeCosmeticTextures()
    {
        var dictionary = new Dictionary<string, string>();
        dictionary.AddEntriesForComponent(PlateElement.Plate, true, true, false);
        dictionary.AddEntriesForComponent(PlateElement.Avatar, true, true, true);
        dictionary.AddEntriesForComponent(PlateElement.Description, true, true, true);
        return dictionary;
    }

    private static void AddEntriesForComponent(this Dictionary<string, string> dict, PlateElement part, bool bg, bool border, bool overlay)
    {
        if (bg)
        {
            foreach (var styleBG in Enum.GetValues<PlateBG>())
            {
                var key = part.ToString() + "_Background_" + styleBG.ToString();
                var value = $"ProfileElements\\{part}\\Background_{styleBG}.png";
                dict[key] = value;
            }
        }

        if (border)
        {
            foreach (var styleBorder in Enum.GetValues<PlateBorder>())
            {
                var key = part.ToString() + "_Border_" + styleBorder.ToString();
                var value = $"ProfileElements\\{part}\\Border_{styleBorder}.png";
                dict[key] = value;
            }
        }

        if (overlay)
        {
            foreach (var styleOverlay in Enum.GetValues<PlateOverlay>())
            {
                var key = part.ToString() + "_Overlay_" + styleOverlay.ToString();
                var value = $"ProfileElements\\{part}\\Overlay_{styleOverlay}.png";
                dict[key] = value;
            }
        }
    }

    public static string ToRichTextString(this EmoteTexture emote)
        => emote switch
        {
            EmoteTexture.Ashamed => ":ashamed:",
            EmoteTexture.Blush => ":blush:",
            EmoteTexture.Bow => ":bow:",
            EmoteTexture.Burrow => ":burrow:",
            EmoteTexture.CatPat => ":catpat:",
            EmoteTexture.Cheer => ":cheer:",
            EmoteTexture.Cute => ":cute:",
            EmoteTexture.Doze => ":doze:",
            EmoteTexture.Eager => ":eager:",
            EmoteTexture.Fight => ":fight:",
            EmoteTexture.Goodnight => ":goodnight:",
            EmoteTexture.Happy => ":happy:",
            EmoteTexture.Hi => ":hi:",
            EmoteTexture.Hmph => ":hmph:",
            EmoteTexture.Huh => ":huh:",
            EmoteTexture.Kiss => ":kiss:",
            EmoteTexture.Laugh => ":laugh:",
            EmoteTexture.Morning => ":morning:",
            EmoteTexture.Needy => ":needy:",
            EmoteTexture.Ok => ":ok:",
            EmoteTexture.Peek => ":peek:",
            EmoteTexture.Pout => ":pout:",
            EmoteTexture.Sweetie => ":sweetie:",
            EmoteTexture.Wah => ":wah:",
            EmoteTexture.Wow => ":wow:",
            EmoteTexture.Yeah => ":yeah:",
            _ => $":{emote.ToString()}:",
        };
}
