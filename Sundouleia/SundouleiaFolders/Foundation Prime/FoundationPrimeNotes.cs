namespace Sundouleia.Gui.Components;

// Racing Brain Head Empty Mind Spiraling Thoughts
// -----------------------------
//
// - DynamicFolder -
// 
// Instead, add a FileSystem function that can ensure folder & child exitance within a folder.
// This way we can have a single function that both ensures a folder's path exists, and one that
// ensures the respective leaves are present within it.
//
// - Selector -
// Split into a selection monitor, filter, state cache. (maybe?)
//
// In general, the goal of the cache and selection is to update it when the filter
// changes, or something is added.
// 
// - Other Notes -
// 
// Might be a good idea to adopt luna's 'flattened tree node hierarchy' structure for its ImSharp.TreeLine class.
// This has a much more efficient way of handling the drawlines, however, it is imparative that we ONLY use it if we can
// display contents of any size with it, otherwise it will be a flop.
// (Additionally be sure to look through all classes thoroughly before attempting to adopt anything).
//
// That is the one benificial thing that luna's drawtree may provide is a more streamlined node draw process with
// interactions, so potentially look into first to verify this!
