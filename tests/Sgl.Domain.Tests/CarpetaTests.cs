using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class CarpetaTests
{
    [Fact]
    public void Registrar_starts_in_Citada_with_pending_decision_and_audit_entry()
    {
        var carpeta = CarpetaFactory.New();

        Assert.Equal(EstadoCarpeta.Citada, carpeta.Estado);
        Assert.Equal(Decision.Pendiente, carpeta.Decision);
        var entry = Assert.Single(carpeta.Historial);
        Assert.Equal("Registro", entry.Campo);
        Assert.Equal("registrador", entry.Usuario);
        Assert.Null(entry.Anterior);
    }

    [Fact]
    public void CambiarEstado_writes_history_with_previous_and_new_value()
    {
        var carpeta = CarpetaFactory.New();
        var at = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);

        carpeta.CambiarEstado(EstadoCarpeta.Revision, "ana", at);

        var entry = carpeta.Historial.Last();
        Assert.Equal("Estado", entry.Campo);
        Assert.Equal("Citada", entry.Anterior);
        Assert.Equal("Revision", entry.Nuevo);
        Assert.Equal("ana", entry.Usuario);
        Assert.Equal(at, entry.Fecha);
    }

    [Fact]
    public void Reaching_Otorgado_sets_decision_Otorgado_and_audits_it()
    {
        var carpeta = CarpetaFactory.InEstado(EstadoCarpeta.Otorgado);

        Assert.Equal(Decision.Otorgado, carpeta.Decision);
        Assert.Contains(carpeta.Historial, h => h.Campo == "Decision" && h.Nuevo == "Otorgado");
    }

    [Fact]
    public void Reaching_Denegado_sets_decision_Denegado()
    {
        var carpeta = CarpetaFactory.InEstado(EstadoCarpeta.Denegado);

        Assert.Equal(Decision.Denegado, carpeta.Decision);
    }

    [Fact]
    public void Invalid_transition_does_not_write_history()
    {
        var carpeta = CarpetaFactory.New();
        var before = carpeta.Historial.Count;

        Assert.Throws<InvalidTransitionException>(() => carpeta.CambiarEstado(EstadoCarpeta.Otorgado, "x", CarpetaFactory.Now));
        Assert.Equal(before, carpeta.Historial.Count);
    }

    [Fact]
    public void Editar_records_one_history_entry_per_changed_field_only()
    {
        var carpeta = CarpetaFactory.New();
        var before = carpeta.Historial.Count;

        carpeta.Editar(Sede.AvArgentina, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 15), "Favorable", "ana", CarpetaFactory.Now);

        Assert.Equal(before + 2, carpeta.Historial.Count);
        Assert.Contains(carpeta.Historial, h => h.Campo == "FechaSubida" && h.Anterior == null && h.Nuevo == "2026-03-15");
        Assert.Contains(carpeta.Historial, h => h.Campo == "IdoneidadMoral" && h.Nuevo == "Favorable");
        Assert.Equal(new DateOnly(2026, 3, 15), carpeta.FechaSubida);
    }

    [Fact]
    public void Editar_sede_is_audited()
    {
        var carpeta = CarpetaFactory.New();

        carpeta.Editar(Sede.Placilla, carpeta.FechaCitacion, null, null, "ana", CarpetaFactory.Now);

        Assert.Equal(Sede.Placilla, carpeta.Sede);
        Assert.Contains(carpeta.Historial, h => h.Campo == "Sede" && h.Anterior == "AvArgentina" && h.Nuevo == "Placilla");
    }

    [Fact]
    public void Editar_without_changes_writes_nothing()
    {
        var carpeta = CarpetaFactory.New();
        var before = carpeta.Historial.Count;

        carpeta.Editar(carpeta.Sede, carpeta.FechaCitacion, carpeta.FechaSubida, carpeta.IdoneidadMoral, "ana", CarpetaFactory.Now);

        Assert.Equal(before, carpeta.Historial.Count);
    }

    [Fact]
    public void Editar_rejects_FechaSubida_before_FechaCitacion()
    {
        var carpeta = CarpetaFactory.New();

        Assert.Throws<DomainException>(() =>
            carpeta.Editar(carpeta.Sede, carpeta.FechaCitacion, new DateOnly(2026, 1, 1), null, "ana", CarpetaFactory.Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Changes_require_an_author(string autor)
    {
        var carpeta = CarpetaFactory.New();

        Assert.Throws<DomainException>(() => carpeta.CambiarEstado(EstadoCarpeta.Revision, autor, CarpetaFactory.Now));
    }

    [Theory]
    [InlineData("", "Pérez")]
    [InlineData("Juan", " ")]
    public void Ciudadano_requires_nombre_and_apellido(string nombre, string apellido)
    {
        Assert.Throws<DomainException>(() => new Ciudadano(Rut.Parse("12345678-5"), nombre, apellido));
    }
}
