using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>R11: undoing a state change restores the previous state and every side effect, audited.</summary>
public class DeshacerEstadoTests
{
    private static readonly DateTime Antes = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cambio = new(2026, 3, 11, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deshace = new(2026, 3, 11, 14, 0, 8, DateTimeKind.Utc);

    private static Carpeta EnArchivos()
    {
        var c = CarpetaFactory.New();
        c.ForzarEstado(EstadoCarpeta.SeEncuentraEnArchivos, "ana", Antes);
        c.MarcarPendienteBusqueda(true, "ana", Antes);
        return c;
    }

    [Fact]
    public void Restoring_returns_state_and_clears_what_the_change_stamped()
    {
        var c = EnArchivos();
        var punto = c.PuntoRestauracion();
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Cambio);
        Assert.NotNull(c.FechaSubida);
        Assert.False(c.PendienteBusqueda);

        c.RestaurarEstado(punto, EstadoCarpeta.SubidaConaset, "ana", Deshace);

        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, c.Estado);
        Assert.Null(c.FechaSubida);
        Assert.Null(c.FechaCierre);
        Assert.True(c.PendienteBusqueda);
        Assert.Equal(punto.FechaEstadoActual, c.FechaEstadoActual);
        Assert.Equal(Bandeja.SeEncuentraEnArchivos, c.Bandeja);
    }

    [Fact]
    public void Restoring_is_audited_with_a_readable_line_and_the_real_author()
    {
        var c = EnArchivos();
        var punto = c.PuntoRestauracion();
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Cambio);

        c.RestaurarEstado(punto, EstadoCarpeta.SubidaConaset, "pedro", Deshace);

        var ultimas = c.Historial.Where(h => h.Fecha == Deshace).ToList();
        Assert.Contains(ultimas, h => h.Campo == "Estado" && h.Anterior == "SubidaConaset" && h.Nuevo == "SeEncuentraEnArchivos" && h.Usuario == "pedro");
        Assert.Contains(ultimas, h => h.Campo == "Deshacer" && h.Nuevo!.Contains("SeEncuentraEnArchivos"));
    }

    [Fact]
    public void Restoring_is_rejected_when_the_state_changed_again_in_the_meantime()
    {
        var c = EnArchivos();
        var punto = c.PuntoRestauracion();
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Cambio);
        c.ForzarEstado(EstadoCarpeta.NoExisteCarpeta, "pedro", Cambio.AddSeconds(3));

        var ex = Assert.Throws<DomainException>(() => c.RestaurarEstado(punto, EstadoCarpeta.SubidaConaset, "ana", Deshace));

        Assert.Contains("cambió", ex.Message);
        Assert.Equal(EstadoCarpeta.NoExisteCarpeta, c.Estado);
    }

    [Fact]
    public void Restoring_keeps_a_FechaSubida_that_existed_before_the_change()
    {
        var c = EnArchivos();
        c.Editar(c.Sede, c.FechaCitacion, new DateOnly(2026, 3, 10), null, "ana", Antes);
        var punto = c.PuntoRestauracion();
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Cambio);

        c.RestaurarEstado(punto, EstadoCarpeta.SubidaConaset, "ana", Deshace);

        Assert.Equal(new DateOnly(2026, 3, 10), c.FechaSubida);
    }

    [Fact]
    public void Restoring_a_final_decision_goes_back_to_pending()
    {
        var c = CarpetaFactory.New();
        var punto = c.PuntoRestauracion();
        c.ForzarEstado(EstadoCarpeta.Otorgado, "ana", Cambio);

        c.RestaurarEstado(punto, EstadoCarpeta.Otorgado, "ana", Deshace);

        Assert.Equal(Decision.Pendiente, c.Decision);
        Assert.Equal(EstadoCarpeta.Citada, c.Estado);
        Assert.Null(c.FechaCierre);
    }

    [Fact]
    public void An_annulled_carpeta_cannot_be_restored_to_a_previous_state()
    {
        var c = EnArchivos();
        var punto = c.PuntoRestauracion();
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Cambio);
        c.Anular("Duplicado", "ana", Cambio);

        Assert.Throws<DomainException>(() => c.RestaurarEstado(punto, EstadoCarpeta.SubidaConaset, "ana", Deshace));
    }
}
