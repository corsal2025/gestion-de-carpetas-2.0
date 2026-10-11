namespace Sgl.Web.Services;

/// <summary>Per-circuit signal raised when the duplicate queue may have changed (badge refresh).</summary>
public sealed class DuplicadosNotifier
{
    public event Action? Changed;

    public void Notify() => Changed?.Invoke();
}
