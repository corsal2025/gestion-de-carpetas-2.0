using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class PendienteBusquedaTests
{
    private static readonly DateTime At = new(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_carpeta_is_not_pendiente_busqueda()
    {
        Assert.False(CarpetaFactory.New().PendienteBusqueda);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, true)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, true)]
    [InlineData(EstadoCarpeta.Citada, false)]
    [InlineData(EstadoCarpeta.Otorgado, false)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta, false)]
    public void PermiteBusquedaPendiente_only_for_archivos_and_of43(EstadoCarpeta estado, bool esperado)
    {
        Assert.Equal(esperado, Carpeta.PermiteBusquedaPendiente(estado));
    }

    [Fact]
    public void Marcar_on_and_off_toggles_and_audits()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);

        c.MarcarPendienteBusqueda(true, "ana", At);
        Assert.True(c.PendienteBusqueda);
        var on = c.Historial.Last();
        Assert.Equal("PendienteBusqueda", on.Campo);
        Assert.Equal("No", on.Anterior);
        Assert.Equal("Si", on.Nuevo);

        c.MarcarPendienteBusqueda(false, "ana", At);
        Assert.False(c.PendienteBusqueda);
    }

    [Fact]
    public void Marcar_same_value_is_idempotent_without_audit()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarPendienteBusqueda(true, "ana", At);
        var count = c.Historial.Count;

        c.MarcarPendienteBusqueda(true, "ana", At);

        Assert.Equal(count, c.Historial.Count);
    }

    [Fact]
    public void Marcar_on_in_non_archive_state_throws()
    {
        var c = CarpetaFactory.New();
        Assert.Throws<DomainException>(() => c.MarcarPendienteBusqueda(true, "ana", At));
        Assert.False(c.PendienteBusqueda);
    }

    [Fact]
    public void Marcar_off_is_always_allowed()
    {
        var c = CarpetaFactory.New();
        c.MarcarPendienteBusqueda(false, "ana", At);
        Assert.False(c.PendienteBusqueda);
    }

    [Fact]
    public void ForzarEstado_to_other_state_clears_mark_and_audits()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarPendienteBusqueda(true, "ana", At);

        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", At);

        Assert.False(c.PendienteBusqueda);
        Assert.Contains(c.Historial, h => h.Campo == "PendienteBusqueda" && h.Nuevo == "No");
    }

    [Fact]
    public void Cleared_mark_does_not_return_when_state_goes_back_to_archivos()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarPendienteBusqueda(true, "ana", At);
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", At);

        c.ForzarEstado(EstadoCarpeta.SeEncuentraEnArchivos, "ana", At);

        Assert.False(c.PendienteBusqueda);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos, EstadoCarpeta.SeEncuentraEnOf43)]
    [InlineData(EstadoCarpeta.SeEncuentraEnOf43, EstadoCarpeta.SeEncuentraEnArchivos)]
    public void ForzarEstado_between_archivos_and_of43_keeps_mark(EstadoCarpeta from, EstadoCarpeta to)
    {
        var c = CarpetaFactory.InEstado(from);
        c.MarcarPendienteBusqueda(true, "ana", At);

        c.ForzarEstado(to, "ana", At);

        Assert.True(c.PendienteBusqueda);
    }

    [Fact]
    public void CambiarEstado_to_other_state_clears_mark()
    {
        // Registrar -> Citada; marca solo es posible en archivo, se fuerza el estado en una carpeta con mark vía ForzarEstado primero.
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);
        c.MarcarPendienteBusqueda(true, "ana", At);
        // CambiarEstado exige transición válida: desde estados de archivo no hay aristas, así que debe lanzar y conservar la marca.
        Assert.Throws<InvalidTransitionException>(() => c.CambiarEstado(EstadoCarpeta.Revision, "ana", At));
        Assert.True(c.PendienteBusqueda);
    }
}
