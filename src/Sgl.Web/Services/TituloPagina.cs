namespace Sgl.Web.Services;

/// <summary>Single format for every browser-tab title: "&lt;Módulo&gt; · Gestión de Carpetas 2.0".</summary>
public static class TituloPagina
{
    public const string Sistema = "Gestión de Carpetas 2.0";

    public static string De(string modulo) =>
        string.IsNullOrWhiteSpace(modulo) ? Sistema : $"{modulo.Trim()} · {Sistema}";
}
