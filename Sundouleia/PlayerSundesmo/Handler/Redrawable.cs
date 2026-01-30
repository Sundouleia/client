using Sundouleia.Pairs.Enums;

namespace Sundouleia.Pairs;

public interface IRedrawable
{
	void RedrawGameObject(Redraw redraw);
	Task<Redraw> ReapplyAlterations();
	event Action<IRedrawable, OwnedObject> OnReapplyRequested;
}