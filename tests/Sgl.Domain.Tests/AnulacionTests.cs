using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class AnulacionTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static Carpeta Agenda(DateOnly fecha, Sede sede = Sede.AvArgentina) =>
        Carpeta.CrearDesdeAgenda(
            new Ciudadano(Rut.Parse("12345678-5"), "JUAN", "PEREZ SOTO"),
            sede, fecha, new TimeOnly(9, 30), TramiteAgenda.Renovacion, "importador", Now);

    [Fact]
    public void Clasificar_without_clase_is_rejected_and_stays_PorClasificar()
    {
        var c = Agenda(new DateOnly(2026, 10, 13));

        var ex = Assert.Throws<DomainException>(() =>
            c.Clasificar(EstadoCarpeta.SubidaConaset, ClaseLicencia.Ninguna, null, null, "javiera", Now));

        Assert.Contains("clase", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoCarpeta.PorClasificar, c.Estado);
        Assert.Equal(ClaseLicencia.Ninguna, c.Clases);
    }

    [Fact]
    public void Clasificar_with_clase_assigns_and_audits_it()
    {
        var c = Agenda(new DateOnly(2026, 10, 13));

        c.Clasificar(EstadoCarpeta.SubidaConaset, ClaseLicencia.B | ClaseLicencia.A2, null, null, "javiera", Now);

        Assert.Equal(ClaseLicencia.B | ClaseLicencia.A2, c.Clases);
        Assert.Contains(c.Historial, h => h.Campo == nameof(ClaseLicencia) && h.Nuevo == "A2, B");
    }

    [Fact]
    public void Anular_marks_soft_deleted_with_reason_author_and_date_keeping_state()
    {
        var c = Agenda(new DateOnly(2026, 10, 13));

        c.Anular(MotivosAnulacion.Duplicado, "javiera", Now);

        Assert.True(c.Anulada);
        Assert.Equal(MotivosAnulacion.Duplicado, c.MotivoAnulacion);
        Assert.Equal("javiera", c.AnuladaPor);
        Assert.Equal(Now, c.AnuladaEn);
        Assert.Equal(EstadoCarpeta.PorClasificar, c.Estado);
        Assert.Contains(c.Historial, h => h.Campo == "Anulacion" && h.Nuevo == "Anulada: Duplicado" && h.Usuario == "javiera");
    }

    [Fact]
    public void Anular_twice_is_rejected()
    {
        var c = Agenda(new DateOnly(2026, 10, 13));
        c.Anular(MotivosAnulacion.Duplicado, "javiera", Now);

        Assert.Throws<DomainException>(() => c.Anular(MotivosAnulacion.Duplicado, "javiera", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Anular_requires_a_reason(string motivo)
    {
        var c = Agenda(new DateOnly(2026, 10, 13));

        Assert.Throws<DomainException>(() => c.Anular(motivo, "javiera", Now));
        Assert.False(c.Anulada);
    }

    [Fact]
    public void Restaurar_returns_to_previous_state_and_is_audited()
    {
        var c = Agenda(new DateOnly(2026, 10, 13));
        c.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, ClaseLicencia.B, null, null, "javiera", Now);
        c.Anular(MotivosAnulacion.Duplicado, "javiera", Now);

        c.Restaurar("raul", Now.AddHours(1));

        Assert.False(c.Anulada);
        Assert.Null(c.MotivoAnulacion);
        Assert.Null(c.AnuladaEn);
        Assert.Null(c.AnuladaPor);
        Assert.Equal(EstadoCarpeta.SeEncuentraEnArchivos, c.Estado);
        Assert.Contains(c.Historial, h => h.Campo == "Anulacion" && h.Anterior == "Anulada: Duplicado" && h.Nuevo == "Restaurada" && h.Usuario == "raul");
    }

    [Fact]
    public void Restaurar_a_non_cancelled_carpeta_is_rejected()
    {
        var c = Agenda(new DateOnly(2026, 10, 13));

        Assert.Throws<DomainException>(() => c.Restaurar("raul", Now));
    }

    [Fact]
    public void Reagendar_moves_date_and_sede_and_records_history_line()
    {
        var c = Agenda(new DateOnly(2026, 9, 2));

        c.Reagendar(new DateOnly(2026, 10, 13), Sede.Placilla, "javiera", Now);

        Assert.Equal(new DateOnly(2026, 10, 13), c.FechaCitacion);
        Assert.Equal(Sede.Placilla, c.Sede);
        Assert.Contains(c.Historial, h => h.Campo == "Reagendamiento" && h.Nuevo == "Reagendada desde 02-09 a 13-10");
        Assert.Contains(c.Historial, h => h.Campo == nameof(Carpeta.FechaCitacion) && h.Anterior == "2026-09-02" && h.Nuevo == "2026-10-13");
        Assert.Contains(c.Historial, h => h.Campo == nameof(Carpeta.Sede) && h.Nuevo == nameof(Sede.Placilla));
    }

    [Fact]
    public void Reagendar_same_sede_does_not_audit_sede()
    {
        var c = Agenda(new DateOnly(2026, 9, 2));

        c.Reagendar(new DateOnly(2026, 10, 13), Sede.AvArgentina, "javiera", Now);

        Assert.DoesNotContain(c.Historial, h => h.Campo == nameof(Carpeta.Sede));
    }

    [Fact]
    public void Reagendar_a_cancelled_carpeta_is_rejected()
    {
        var c = Agenda(new DateOnly(2026, 9, 2));
        c.Anular(MotivosAnulacion.Duplicado, "javiera", Now);

        Assert.Throws<DomainException>(() => c.Reagendar(new DateOnly(2026, 10, 13), Sede.AvArgentina, "javiera", Now));
    }

    [Fact]
    public void EstaAbiertaSinArchivar_excludes_cancelled_closed_and_archived()
    {
        var abierta = Agenda(new DateOnly(2026, 10, 13));
        Assert.True(abierta.EstaAbiertaSinArchivar);

        var anulada = Agenda(new DateOnly(2026, 10, 13));
        anulada.Anular(MotivosAnulacion.Duplicado, "javiera", Now);
        Assert.False(anulada.EstaAbiertaSinArchivar);

        var archivada = CarpetaFactory.New();
        archivada.AsignarCaja("CAJA 12", "javiera", Now);
        Assert.False(archivada.EstaAbiertaSinArchivar);

        var enCola = CarpetaFactory.New();
        enCola.AsignarCaja(Carpeta.ColaDeCaja, "javiera", Now);
        Assert.True(enCola.EstaAbiertaSinArchivar);

        Assert.False(CarpetaFactory.InEstado(EstadoCarpeta.Otorgado).EstaAbiertaSinArchivar);
    }
}
