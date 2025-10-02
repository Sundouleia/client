namespace Sundouleia.Gui.Components;

// Remodel this to use a CKFS changelog structure.
// Also should probably add a builder for version entries.
public class Changelog
{
    public List<VersionEntry> Versions { get; private set; } = new List<VersionEntry>();

    public Changelog()
    {

        // append the version information here.
        AddVersionData();
    }

    public VersionEntry VersionEntry(int versionMajor, int versionMinor, int minorUpdate, int updateImprovements)
    {
        var entry = new VersionEntry(versionMajor, versionMinor, minorUpdate, updateImprovements);
        Versions.Add(entry);
        return entry;
    }

    // Add Version Data here.
    private void AddVersionData()
    {
        VersionEntry(0,0,1,0)
            .RegisterMain("Hush-Hush Initial Sundouleia Testing")
            .RegisterFeature("Expect many things to not be finished, is primarily here to showcase it works.");
    }
}
