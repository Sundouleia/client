namespace Sundouleia.Radar;

public class RadarUserComparer : IEqualityComparer<RadarUser>
{
    private static RadarUserComparer _instance = new();

    private RadarUserComparer()
    { }

    public static RadarUserComparer Instance => _instance;

    public bool Equals(RadarUser? x, RadarUser? y)
    {
        if (x is null || y is null) return false;
        return x.UID.Equals(y.UID, StringComparison.Ordinal);
    }

    public int GetHashCode(RadarUser obj)
    {
        return obj.UID.GetHashCode();
    }
}
