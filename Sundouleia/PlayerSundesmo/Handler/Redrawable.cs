using Sundouleia.Pairs.Enums;

namespace Sundouleia.Pairs;

public interface IRedrawable
{
	void RedrawGameObject(RedrawKind redraw);
	Task<RedrawKind> ReapplyAlterations();
	event Action<IRedrawable, OwnedObject> OnReapplyRequested;
}