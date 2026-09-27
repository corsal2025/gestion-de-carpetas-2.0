namespace Sgl.Web.Services;

/// <summary>
/// Name of the person making changes during this browser session. There is no authentication
/// in this module yet, so the author is recorded as plain text in the change history.
/// </summary>
public sealed class AutorState
{
    public string Nombre { get; set; } = string.Empty;

    public bool HasNombre => !string.IsNullOrWhiteSpace(Nombre);
}
