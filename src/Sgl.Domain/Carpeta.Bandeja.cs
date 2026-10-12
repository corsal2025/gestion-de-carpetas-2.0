using System.Linq.Expressions;

namespace Sgl.Domain;

public sealed partial class Carpeta
{
    /// <summary>Placeholder caja for folders queued to be boxed; not yet physically archived.</summary>
    public const string ColaDeCaja = "Cola de Caja";

    /// <summary>The tray this carpeta currently belongs to. Moves automatically with its state.</summary>
    public Bandeja Bandeja => ClasificarBandeja(Estado, Decision, CajaArchivo);

    /// <summary>
    /// Precedence: Por clasificar (agenda, only by state) &gt; Archivadas (real caja) &gt; Cerradas (final decision) &gt; Sin carpeta (F8) &gt; Subidas
    /// &gt; Cambio de domicilio &gt; Cambio dom. solicitado &gt; Por preparar. "Pendiente de búsqueda" is only a same-day flag, not a tray;
    /// location states (SeEncuentraEnArchivos/Of43) stay in Por preparar.
    /// </summary>
    public static Bandeja ClasificarBandeja(EstadoCarpeta estado, Decision decision, string? caja)
    {
        if (estado == EstadoCarpeta.PorClasificar) return Bandeja.PorClasificar;
        if (!string.IsNullOrWhiteSpace(caja) && caja != ColaDeCaja) return Bandeja.Archivadas;
        if (decision is Decision.Otorgado or Decision.Denegado) return Bandeja.Cerradas;
        if (estado == EstadoCarpeta.NoExisteCarpeta) return Bandeja.SinCarpetaF8;
        if (EsEstadoSubidaBandeja(estado)) return Bandeja.Subidas;
        if (estado == EstadoCarpeta.CambioDomicilio) return Bandeja.CambioDomicilio;
        if (estado == EstadoCarpeta.CambioDomicilioSolicitado) return Bandeja.CambioDomicilioSolicitado;
        return Bandeja.PorPreparar;
    }

    private static bool EsEstadoSubidaBandeja(EstadoCarpeta e) =>
        e is EstadoCarpeta.SubidaConaset or EstadoCarpeta.SubidaConF8 or EstadoCarpeta.SubidaConOficio
            or EstadoCarpeta.CambioDomSubidoConaset or EstadoCarpeta.CambioDomSubidoCorreo;

    /// <summary>Same rules as <see cref="ClasificarBandeja"/>, translatable to SQL by EF Core.</summary>
    public static readonly Expression<Func<Carpeta, Bandeja>> BandejaExpresion = c =>
        c.Estado == EstadoCarpeta.PorClasificar ? Bandeja.PorClasificar
        : c.CajaArchivo != null && c.CajaArchivo.Trim() != "" && c.CajaArchivo != ColaDeCaja ? Bandeja.Archivadas
        : c.Decision == Decision.Otorgado || c.Decision == Decision.Denegado ? Bandeja.Cerradas
        : c.Estado == EstadoCarpeta.NoExisteCarpeta ? Bandeja.SinCarpetaF8
        : c.Estado == EstadoCarpeta.SubidaConaset
            || c.Estado == EstadoCarpeta.SubidaConF8
            || c.Estado == EstadoCarpeta.SubidaConOficio
            || c.Estado == EstadoCarpeta.CambioDomSubidoConaset
            || c.Estado == EstadoCarpeta.CambioDomSubidoCorreo ? Bandeja.Subidas
        : c.Estado == EstadoCarpeta.CambioDomicilio ? Bandeja.CambioDomicilio
        : c.Estado == EstadoCarpeta.CambioDomicilioSolicitado ? Bandeja.CambioDomicilioSolicitado
        : Bandeja.PorPreparar;

    /// <summary>SQL-translatable predicate selecting the carpetas of one tray.</summary>
    public static Expression<Func<Carpeta, bool>> EnBandeja(Bandeja bandeja) =>
        Expression.Lambda<Func<Carpeta, bool>>(
            Expression.Equal(BandejaExpresion.Body, Expression.Constant(bandeja)),
            BandejaExpresion.Parameters);
}
