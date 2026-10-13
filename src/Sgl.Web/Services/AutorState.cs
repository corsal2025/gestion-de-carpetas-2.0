namespace Sgl.Web.Services;

/// <summary>
/// Name of the person making changes during this browser session. There is no authentication
/// in this module yet, so the author is recorded as plain text in the change history.
/// The name is optional: when nothing is typed, changes are recorded under <see cref="Predeterminado"/>
/// and no screen blocks the user asking for it.
/// </summary>
public sealed class AutorState
{
    public const string Predeterminado = "Funcionario";

    private string _ingresado = string.Empty;

    /// <summary>Raw text typed in the header field (what the input is bound to).</summary>
    public string Ingresado
    {
        get => _ingresado;
        set => _ingresado = value ?? string.Empty;
    }

    /// <summary>Author recorded in the history: the typed name, or the default one.</summary>
    public string Nombre
    {
        get => HasIngresado ? _ingresado.Trim() : Predeterminado;
        set => _ingresado = value ?? string.Empty;
    }

    public bool HasIngresado => !string.IsNullOrWhiteSpace(_ingresado);

    /// <summary>Always true: a missing name never blocks an action (kept so existing call sites stay valid).</summary>
    public bool HasNombre => true;
}
