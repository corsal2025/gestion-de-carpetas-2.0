using System.Linq.Expressions;

namespace Sgl.Domain;

public sealed partial class Carpeta
{
    /// <summary>Placeholder caja for folders queued to be boxed; not yet physically archived.</summary>
    public const string ColaDeCaja = "Cola de Caja";

    /// <summary>The tray this carpeta currently belongs to. Moves automatically with its state.</summary>
    public Bandeja Bandeja => ClasificarBandeja(Estado, Decision, CajaArchivo, PendienteBusqueda);

    /// <summary>
    /// Precedence: Archivadas (real caja) &gt; Cerradas (final decision) &gt; En búsqueda &gt; Subidas
    /// &gt; Cambio de domicilio &gt; Por preparar.
    /// </summary>
    public static Bandeja ClasificarBandeja(EstadoCarpeta estado, Decision decision, string? caja, bool pendienteBusqueda)
    {
        if (!string.IsNullOrWhiteSpace(caja) && caja != ColaDeCaja) return Bandeja.Archivadas;
        if (decision is Decision.Otorgado or Decision.Denegado) return Bandeja.Cerradas;
        if (pendienteBusqueda || EsEstadoBusqueda(estado)) return Bandeja.EnBusqueda;
        if (EsEstadoSubidaBandeja(estado)) return Bandeja.Subidas;
        if (estado is EstadoCarpeta.CambioDomicilio or EstadoCarpeta.CambioDomicilioSolicitado) return Bandeja.CambioDomicilio;
        return Bandeja.PorPreparar;
    }

    private static bool EsEstadoBusqueda(EstadoCarpeta e) =>
        e is EstadoCarpeta.SeEncuentraEnArchivos or EstadoCarpeta.SeEncuentraEnOf43 or EstadoCarpeta.NoExisteCarpeta;

    private static bool EsEstadoSubidaBandeja(EstadoCarpeta e) =>
        e is EstadoCarpeta.SubidaConaset or EstadoCarpeta.SubidaConF8 or EstadoCarpeta.SubidaConOficio
            or EstadoCarpeta.CambioDomSubidoConaset or EstadoCarpeta.CambioDomSubidoCorreo;

    /// <summary>Same rules as <see cref="ClasificarBandeja"/>, translatable to SQL by EF Core.</summary>
    public static readonly Expression<Func<Carpeta, Bandeja>> BandejaExpresion = c =>
        c.CajaArchivo != null && c.CajaArchivo.Trim() != "" && c.CajaArchivo != ColaDeCaja ? Bandeja.Archivadas
        : c.Decision == Decision.Otorgado || c.Decision == Decision.Denegado ? Bandeja.Cerradas
        : c.PendienteBusqueda
            || c.Estado == EstadoCarpeta.SeEncuentraEnArchivos
            || c.Estado == EstadoCarpeta.SeEncuentraEnOf43
            || c.Estado == EstadoCarpeta.NoExisteCarpeta ? Bandeja.EnBusqueda
        : c.Estado == EstadoCarpeta.SubidaConaset
            || c.Estado == EstadoCarpeta.SubidaConF8
            || c.Estado == EstadoCarpeta.SubidaConOficio
            || c.Estado == EstadoCarpeta.CambioDomSubidoConaset
            || c.Estado == EstadoCarpeta.CambioDomSubidoCorreo ? Bandeja.Subidas
        : c.Estado == EstadoCarpeta.CambioDomicilio || c.Estado == EstadoCarpeta.CambioDomicilioSolicitado ? Bandeja.CambioDomicilio
        : Bandeja.PorPreparar;

    /// <summary>SQL-translatable predicate selecting the carpetas of one tray.</summary>
    public static Expression<Func<Carpeta, bool>> EnBandeja(Bandeja bandeja) =>
        Expression.Lambda<Func<Carpeta, bool>>(
            Expression.Equal(BandejaExpresion.Body, Expression.Constant(bandeja)),
            BandejaExpresion.Parameters);
}
