namespace Sgl.Web.Services;

/// <summary>
/// Name of the person making changes during this browser session. Without authentication
/// (the default) the author is recorded as plain text in the change history; with
/// Seguridad:RequerirAutenticacion it is the Windows user (see <see cref="FijarAutenticado"/>).
/// The name is optional: when nothing is typed, changes are recorded under <see cref="Predeterminado"/>
/// and no screen blocks the user asking for it.
/// </summary>
public sealed class AutorState
{
    public const string Predeterminado = "Funcionario";

    private string _ingresado = string.Empty;
    private string? _autenticado;

    /// <summary>
    /// Usuario de Windows cuando Seguridad:RequerirAutenticacion está activo. Si existe, es el autor
    /// del historial y no se puede cambiar a mano.
    /// </summary>
    public void FijarAutenticado(string? usuario) =>
        _autenticado = string.IsNullOrWhiteSpace(usuario) ? null : usuario.Trim();

    /// <summary>Raw text typed in the header field (what the input is bound to).</summary>
    public string Ingresado
    {
        get => _ingresado;
        set { if (_autenticado is null) _ingresado = value ?? string.Empty; }
    }

    /// <summary>Author recorded in the history: the typed name, or the default one.</summary>
    public string Nombre
    {
        get => _autenticado ?? (HasIngresado ? _ingresado.Trim() : Predeterminado);
        set { if (_autenticado is null) _ingresado = value ?? string.Empty; }
    }

    public bool HasIngresado => _autenticado is not null || !string.IsNullOrWhiteSpace(_ingresado);

    /// <summary>Always true: a missing name never blocks an action (kept so existing call sites stay valid).</summary>
    public bool HasNombre => true;
}
