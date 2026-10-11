using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class ClaseLicenciaTests
{
    [Fact]
    public void New_carpeta_has_no_clase_by_default()
    {
        var carpeta = CarpetaFactory.New();

        Assert.Equal(ClaseLicencia.Ninguna, carpeta.Clases);
    }

    [Fact]
    public void Registrar_with_clases_stores_them()
    {
        var carpeta = Carpeta.Registrar(
            new Ciudadano(Rut.Parse("12345678-5"), "Juan", "Pérez"),
            Sede.AvArgentina,
            new DateOnly(2026, 3, 10),
            "registrador",
            CarpetaFactory.Now,
            ClaseLicencia.A2 | ClaseLicencia.B);

        Assert.Equal(ClaseLicencia.A2 | ClaseLicencia.B, carpeta.Clases);
    }

    [Fact]
    public void Registrar_sets_fecha_registro()
    {
        var carpeta = CarpetaFactory.New();

        Assert.Equal(CarpetaFactory.Now, carpeta.FechaRegistro);
    }

    [Fact]
    public void AsignarClases_audits_previous_and_new_value()
    {
        var carpeta = CarpetaFactory.New();
        carpeta.AsignarClases(ClaseLicencia.B | ClaseLicencia.C, "ana", CarpetaFactory.Now);

        var entry = carpeta.Historial.Last();
        Assert.Equal("ClaseLicencia", entry.Campo);
        Assert.Null(entry.Anterior);
        Assert.Equal("B, C", entry.Nuevo);
        Assert.Equal("ana", entry.Usuario);
        Assert.Equal(ClaseLicencia.B | ClaseLicencia.C, carpeta.Clases);
    }

    [Fact]
    public void AsignarClases_with_same_value_does_not_audit()
    {
        var carpeta = CarpetaFactory.New();
        var count = carpeta.Historial.Count;

        carpeta.AsignarClases(ClaseLicencia.Ninguna, "ana", CarpetaFactory.Now);

        Assert.Equal(count, carpeta.Historial.Count);
    }

    [Fact]
    public void AsignarClases_rejects_undefined_bits()
    {
        var carpeta = CarpetaFactory.New();

        Assert.Throws<DomainException>(() => carpeta.AsignarClases((ClaseLicencia)4096, "ana", CarpetaFactory.Now));
    }

    [Theory]
    [InlineData(ClaseLicencia.A2 | ClaseLicencia.B, ClaseLicencia.B, true)]
    [InlineData(ClaseLicencia.A2, ClaseLicencia.B, false)]
    [InlineData(ClaseLicencia.Ninguna, ClaseLicencia.B, false)]
    public void Overlaps_detects_shared_classes(ClaseLicencia a, ClaseLicencia b, bool expected)
    {
        Assert.Equal(expected, ClasesLicencia.Overlaps(a, b));
    }

    [Fact]
    public void Format_lists_classes_in_order()
    {
        Assert.Equal("A1, B, F", ClasesLicencia.Format(ClaseLicencia.F | ClaseLicencia.A1 | ClaseLicencia.B));
        Assert.Equal(string.Empty, ClasesLicencia.Format(ClaseLicencia.Ninguna));
    }

    [Fact]
    public void Carpeta_is_abierta_while_decision_pending()
    {
        Assert.True(CarpetaFactory.New().EstaAbierta);
        Assert.False(CarpetaFactory.InEstado(EstadoCarpeta.Otorgado).EstaAbierta);
        Assert.False(CarpetaFactory.InEstado(EstadoCarpeta.Denegado).EstaAbierta);
    }

    [Fact]
    public void RevisionDuplicado_records_author_and_timestamp_and_can_be_renewed()
    {
        var ciudadanoId = Guid.NewGuid();
        var revision = new RevisionDuplicado(ciudadanoId, "ana", CarpetaFactory.Now);
        Assert.Equal("ana", revision.RevisadoPor);
        Assert.Equal(CarpetaFactory.Now, revision.RevisadoEn);

        var later = CarpetaFactory.Now.AddDays(1);
        revision.Renovar("pedro", later);
        Assert.Equal("pedro", revision.RevisadoPor);
        Assert.Equal(later, revision.RevisadoEn);
        Assert.Throws<DomainException>(() => revision.Renovar(" ", later));
    }
}
