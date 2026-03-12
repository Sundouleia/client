using CkCommons.Textures;
using Sundouleia.Pairs;

namespace Sundouleia.CustomCombos;

public abstract class LociComboEditorBase<T> : CkFilterComboCache<T>
{
    protected float IconScale;

    protected LociComboEditorBase(ILogger log, float scale, Func<IReadOnlyList<T>> generator)
        : base(generator, log)
    {
        IconScale = scale;
        Current = default;
    }

    protected virtual Vector2 IconSize => LociIcon.Size * IconScale;
}
