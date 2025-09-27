namespace Sundouleia.Interop; 
public interface IIpcCaller : IDisposable
{
    static bool APIAvailable { get; }
    void CheckAPI();
}
