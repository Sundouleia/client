namespace Sundouleia.PlayerClient;

[Flags]
public enum AlertKind
{
    None    = 0 << 0,
    Bubble  = 1 << 0,
    DtrBar  = 1 << 1,
    Audio   = 1 << 2,
}

