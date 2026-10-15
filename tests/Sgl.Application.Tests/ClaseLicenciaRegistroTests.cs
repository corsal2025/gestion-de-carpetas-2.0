using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

public sealed class ClaseLicenciaRegistroTests : IDisposable
{
    private const string RutA = "12.345.678-5";

    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static RegistrarCarpetaCommand Cmd(
        ClaseLicencia clases = ClaseLicencia.Ninguna,
        Sede sede = Sede.AvArgentina,
        bool confirmar = false,
        string rut = RutA) =>
        new(rut, "Juan", "Pérez", sede, new DateOnly(2026, 5, 4), "ana", clases, confirmar);

    private static LicenciasOptions ObligatoriaDesde(int year, int month, int day) =>
        new() { ClaseObligatoriaDesde = new DateOnly(year, month, day) };

    [Fact]
    public async Task Registrar_persists_clases()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B | ClaseLicencia.C));

        var detalle = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(ClaseLicencia.B | ClaseLicencia.C, detalle.Clases);
    }

    [Fact]
    public async Task Registrar_without_clase_is_allowed_when_no_mandatory_date()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Registrar_without_clase_is_allowed_before_mandatory_date()
    {
        var id = await _db.NewService(ObligatoriaDesde(2026, 5, 5)).RegistrarAsync(Cmd());

        Assert.NotEqual(Guid.Empty, id);
    }

    [Theory]
    [InlineData(2026, 5, 4)]
    [InlineData(2026, 1, 1)]
    public async Task Registrar_without_clase_fails_from_mandatory_date(int y, int m, int d)
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _db.NewService(ObligatoriaDesde(y, m, d)).RegistrarAsync(Cmd()));

        Assert.Contains("clase", ex.Message, StringComparison.OrdinalIgnoreCase);
        await using var ctx = _db.NewContext();
        Assert.Equal(0, await ctx.Carpetas.CountAsync());
    }

    [Fact]
    public async Task Registrar_with_clase_succeeds_from_mandatory_date()
    {
        var id = await _db.NewService(ObligatoriaDesde(2026, 1, 1)).RegistrarAsync(Cmd(ClaseLicencia.B));

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Registrar_same_rut_overlapping_clase_with_open_carpeta_is_blocked_in_any_sede()
    {
        var existente = await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.A2 | ClaseLicencia.B, Sede.Placilla));

        var ex = await Assert.ThrowsAsync<CarpetaDuplicadaException>(() =>
            _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B, Sede.AvArgentina, confirmar: true)));

        Assert.True(ex.Bloqueante);
        var conflicto = Assert.Single(ex.Verificacion.Bloqueantes);
        Assert.Equal(existente, conflicto.Id);
        Assert.Equal(Sede.Placilla, conflicto.Sede);
        Assert.Equal(EstadoCarpeta.Citada, conflicto.Estado);
        Assert.Equal(new DateOnly(2026, 5, 4), conflicto.FechaCitacion);
        await using var ctx = _db.NewContext();
        Assert.Equal(1, await ctx.Carpetas.CountAsync());
    }

    [Fact]
    public async Task Registrar_same_rut_different_clase_requires_confirmation()
    {
        await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.A2));

        var ex = await Assert.ThrowsAsync<CarpetaDuplicadaException>(() =>
            _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B)));

        Assert.False(ex.Bloqueante);
        Assert.Single(ex.Verificacion.Advertencias);
        Assert.Empty(ex.Verificacion.Bloqueantes);
    }

    [Fact]
    public async Task Registrar_same_rut_open_carpeta_without_clase_requires_confirmation()
    {
        await _db.NewService().RegistrarAsync(Cmd());

        var ex = await Assert.ThrowsAsync<CarpetaDuplicadaException>(() =>
            _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B)));

        Assert.False(ex.Bloqueante);
    }

    [Fact]
    public async Task Registrar_with_warning_succeeds_when_confirmed()
    {
        await _db.NewService().RegistrarAsync(Cmd());

        await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B, confirmar: true));

        await using var ctx = _db.NewContext();
        Assert.Equal(2, await ctx.Carpetas.CountAsync());
    }

    [Fact]
    public async Task Registrar_ignores_closed_carpetas()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B));
        await _db.NewService().CambiarDecisionAsync(id, Decision.Otorgado, "ana");

        var nuevo = await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B));

        Assert.NotEqual(id, nuevo);
    }

    [Fact]
    public async Task Registrar_different_rut_is_not_affected()
    {
        await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B));

        var id = await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B, rut: "11.111.111-1"));

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task VerificarDuplicado_classifies_open_carpetas()
    {
        await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.B, Sede.Placilla));
        await _db.NewService().RegistrarAsync(Cmd(ClaseLicencia.A2, confirmar: true));

        var v = await _db.NewService().VerificarDuplicadoAsync(RutA, ClaseLicencia.B);

        Assert.True(v.Bloquea);
        Assert.Single(v.Bloqueantes);
        Assert.Single(v.Advertencias);
    }

    [Fact]
    public async Task VerificarDuplicado_for_unknown_rut_is_empty()
    {
        var v = await _db.NewService().VerificarDuplicadoAsync(RutA, ClaseLicencia.B);

        Assert.False(v.Bloquea);
        Assert.False(v.RequiereConfirmacion);
    }

    [Fact]
    public async Task AsignarClases_persists_and_audits()
    {
        var id = await _db.NewService().RegistrarAsync(Cmd());

        await _db.NewService().AsignarClasesAsync(id, ClaseLicencia.A1 | ClaseLicencia.B, "pedro");

        var detalle = await _db.NewService().ObtenerAsync(id);
        Assert.Equal(ClaseLicencia.A1 | ClaseLicencia.B, detalle.Clases);
        Assert.Contains(detalle.Historial, h => h.Campo == "ClaseLicencia" && h.Nuevo == "A1, B" && h.Usuario == "pedro");
    }
}
