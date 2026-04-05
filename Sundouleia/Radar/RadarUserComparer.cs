namespace Sundouleia.Radar;

public class RadarUserComparer : IEqualityComparer<RadarPublicUser>
{
    private static RadarUserComparer _instance = new();

    private RadarUserComparer()
    { }

    public static RadarUserComparer Instance => _instance;

    public bool Equals(RadarPublicUser? x, RadarPublicUser? y)
    {
        if (x is null || y is null) return false;
        return x.UID.Equals(y.UID, StringComparison.Ordinal);
    }

    public int GetHashCode(RadarPublicUser obj)
    {
        return obj.UID.GetHashCode();
    }
}
