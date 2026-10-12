using System.Linq.Expressions;

namespace Sgl.Domain;

public sealed partial class Carpeta
{
    /// <summary>Placeholder caja for folders queued to be boxed; not yet physically archived.</summary>
    public const string ColaDeCaja = "Cola de Caja";

    /// <summary>The tray this carpeta currently belongs to. Moves automatically with its state.</summary>
    public Bandeja Bandeja => ClasificarBandeja(Estado, Decision, CajaArchivo);

    /// <summary>
    /// Precedence: Por clasificar (agenda, only by state) &gt; Archivadas (real caja) &gt; Cerradas (final decision) &gt; one tray per
    /// action estado (subidas, cambio de domicilio, ubicación, certificado, canje, sin carpeta) &gt; Por preparar (1° Licencia).
    /// "Pendiente de búsqueda" is only a same-day flag, not a tray.
    /// </summary>
    public static Bandeja ClasificarBandeja(EstadoCarpeta estado, Decision decision, string? caja)
    {
        if (estado == EstadoCarpeta.PorClasificar) return Bandeja.PorClasificar;
        if (!string.IsNullOrWhiteSpace(caja) && caja != ColaDeCaja) return Bandeja.Archivadas;
        if (decision is Decision.Otorgado or Decision.Denegado) return Bandeja.Cerradas;
        return BandejaPorEstado(estado);
    }

    private static Bandeja BandejaPorEstado(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.NoExisteCarpeta => Bandeja.SinCarpetaF8,
        EstadoCarpeta.SubidaConaset => Bandeja.SubidaConaset,
        EstadoCarpeta.SubidaConF8 => Bandeja.SubidaConF8,
        EstadoCarpeta.SubidaConOficio => Bandeja.SubidaConOficio,
        EstadoCarpeta.CambioDomSubidoConaset => Bandeja.CambioDomSubidoConaset,
        EstadoCarpeta.CambioDomSubidoCorreo => Bandeja.CambioDomSubidoCorreo,
        EstadoCarpeta.CambioDomicilio => Bandeja.CambioDomicilio,
        EstadoCarpeta.CambioDomicilioSolicitado => Bandeja.CambioDomicilioSolicitado,
        EstadoCarpeta.SeEncuentraEnArchivos => Bandeja.SeEncuentraEnArchivos,
        EstadoCarpeta.SeEncuentraEnOf43 => Bandeja.SeEncuentraEnOf43,
        EstadoCarpeta.CrearCertificado => Bandeja.CrearCertificado,
        EstadoCarpeta.CanjeLicExtranjera => Bandeja.CanjeLicExtranjera,
        _ => Bandeja.PorPreparar,
    };

    /// <summary>Same rules as <see cref="ClasificarBandeja"/>, translatable to SQL by EF Core.</summary>
    public static readonly Expression<Func<Carpeta, Bandeja>> BandejaExpresion = c =>
        c.Estado == EstadoCarpeta.PorClasificar ? Bandeja.PorClasificar
        : c.CajaArchivo != null && c.CajaArchivo.Trim() != "" && c.CajaArchivo != ColaDeCaja ? Bandeja.Archivadas
        : c.Decision == Decision.Otorgado || c.Decision == Decision.Denegado ? Bandeja.Cerradas
        : c.Estado == EstadoCarpeta.NoExisteCarpeta ? Bandeja.SinCarpetaF8
        : c.Estado == EstadoCarpeta.SubidaConaset ? Bandeja.SubidaConaset
        : c.Estado == EstadoCarpeta.SubidaConF8 ? Bandeja.SubidaConF8
        : c.Estado == EstadoCarpeta.SubidaConOficio ? Bandeja.SubidaConOficio
        : c.Estado == EstadoCarpeta.CambioDomSubidoConaset ? Bandeja.CambioDomSubidoConaset
        : c.Estado == EstadoCarpeta.CambioDomSubidoCorreo ? Bandeja.CambioDomSubidoCorreo
        : c.Estado == EstadoCarpeta.CambioDomicilio ? Bandeja.CambioDomicilio
        : c.Estado == EstadoCarpeta.CambioDomicilioSolicitado ? Bandeja.CambioDomicilioSolicitado
        : c.Estado == EstadoCarpeta.SeEncuentraEnArchivos ? Bandeja.SeEncuentraEnArchivos
        : c.Estado == EstadoCarpeta.SeEncuentraEnOf43 ? Bandeja.SeEncuentraEnOf43
        : c.Estado == EstadoCarpeta.CrearCertificado ? Bandeja.CrearCertificado
        : c.Estado == EstadoCarpeta.CanjeLicExtranjera ? Bandeja.CanjeLicExtranjera
        : Bandeja.PorPreparar;

    /// <summary>SQL-translatable twin of <see cref="EstaAbiertaSinArchivar"/>.</summary>
    public static readonly Expression<Func<Carpeta, bool>> AbiertaSinArchivarExpresion = c =>
        !c.Anulada
        && c.Decision == Decision.Pendiente
        && (c.CajaArchivo == null || c.CajaArchivo.Trim() == "" || c.CajaArchivo == ColaDeCaja);

    /// <summary>SQL-translatable predicate selecting the carpetas of one tray.</summary>
    public static Expression<Func<Carpeta, bool>> EnBandeja(Bandeja bandeja) =>
        Expression.Lambda<Func<Carpeta, bool>>(
            Expression.Equal(BandejaExpresion.Body, Expression.Constant(bandeja)),
            BandejaExpresion.Parameters);
}
