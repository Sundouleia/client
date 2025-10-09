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