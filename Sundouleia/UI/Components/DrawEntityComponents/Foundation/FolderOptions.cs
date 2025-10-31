namespace Sundouleia.Gui.Components;

/// <summary>
///     Generic options given to a <see cref="DynamicFolder"/> to control its behavior."/>
/// </summary>
public readonly record struct FolderOptions(bool ShowIfEmpty, bool IsDropTarget, bool DragDropItems, bool MultiSelect)
{
    public static FolderOptions Default => new(false, false, false, false);
    public static FolderOptions DefaultShowEmpty => new(true, false, false, false);
    public static FolderOptions FolderEditor => new(true, true, true, true);
}