using Microsoft.EntityFrameworkCore;
using Sgl.Application.Asistencias;
using Sgl.Application.Carpetas;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>E2 · Asistencia del día. Datos 100% sintéticos; el reloj de TestDb es el 2026-05-04 (hora Chile 06:00).</summary>
public sealed class AsistenciaDelDiaTests : IDisposable
{
    private static readonly DateOnly Hoy = new(2026, 5, 4);
    private static readonly DateTime Ayer = new(2026, 5, 3, 15, 0, 0, DateTimeKind.Utc);
    private const string RutAna = "12345678-5";
    private const string RutBruno = "11111111-1";
    private const string RutNuevo = "22222222-2";
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private Guid Sembrar(string rut, DateOnly fecha, string nombre = "Ana", string apellido = "Prueba", Sede sede = Sede.Placilla)
    {
        using var ctx = _db.NewContext();
        var ciudadano = ctx.Ciudadanos.AsEnumerable().FirstOrDefault(c => c.Rut.Value == rut) ?? new Ciudadano(Rut.Parse(rut), nombre, apellido);
        var c = Carpeta.CrearDesdeAgenda(ciudadano, sede, fecha, new TimeOnly(9, 30), TramiteAgenda.Renovacion, "imp", Ayer);
        ctx.Carpetas.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    private Guid SembrarArchivada(string rut, string caja)
    {
        using var ctx = _db.NewContext();
        var ciudadano = ctx.Ciudadanos.AsEnumerable().First(c => c.Rut.Value == rut);
        var c = Carpeta.Registrar(ciudadano, Sede.AvArgentina, new DateOnly(2025, 1, 10), "imp", Ayer);
        c.CambiarDecision(Decision.Otorgado, "imp", Ayer);
        c.AsignarCaja(caja, "imp", Ayer);
        ctx.Carpetas.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    private static AgregarExtraCommand Extra(string rut, string? nombre = null, string tramite = "Renovación", string autor = "Funcionario Uno") =>
        new(rut, nombre, Sede.Placilla, tramite, autor);

    // Criterio 1
    [Fact]
    public async Task Buscar_ReutilizaBuscarGlobal_ConCitaDeHoyYOtrasCarpetasDeTodaBandejaIncluidaArchivada()
    {
        var hoy = Sembrar(RutAna, Hoy);
        var archivada = SembrarArchivada(RutAna, "AA-12");
        Sembrar(RutBruno, Hoy, "Bruno", "Muestra");

        var r = await _db.NewAsistenciaService().BuscarAsync("12.345.678-5");

        Assert.Equal("12.345.678-5", r.Rut);
        Assert.True(r.Existe);
        Assert.Equal("ANA PRUEBA", r.Nombre);
        Assert.Equal(hoy, r.CitaHoy!.CarpetaId);
        Assert.Equal(Sede.Placilla, r.CitaHoy.Sede);
        Assert.Equal(new TimeOnly(9, 30), r.CitaHoy.Hora);
        Assert.Equal(Bandeja.PorClasificar, r.CitaHoy.Bandeja);
        var otra = Assert.Single(r.Otras);
        Assert.Equal(archivada, otra.Id);
        Assert.Equal(Bandeja.Archivadas, otra.Bandeja);
        Assert.Null(r.Marcada);
    }

    [Fact]
    public async Task Buscar_CiudadanoSoloConCarpetaAntigua_NoTieneCitaDeHoyPeroListaSusCarpetas()
    {
        var antigua = Sembrar(RutAna, new DateOnly(2025, 3, 3));

        var r = await _db.NewAsistenciaService().BuscarAsync(RutAna);

        Assert.True(r.Existe);
        Assert.Null(r.CitaHoy);
        Assert.Equal(antigua, Assert.Single(r.Otras).Id);
    }

    [Fact]
    public async Task Buscar_RutInexistente_NoExisteYNoDevuelveCarpetas()
    {
        var r = await _db.NewAsistenciaService().BuscarAsync(RutNuevo);

        Assert.False(r.Existe);
        Assert.Null(r.Nombre);
        Assert.Null(r.CitaHoy);
        Assert.Empty(r.Otras);
    }

    [Fact]
    public async Task Buscar_DvInvalido_LanzaErrorDeNegocioSinBuscar()
    {
        Sembrar(RutAna, Hoy);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().BuscarAsync("12.345.678-9"));

        Assert.Contains("dígito verificador", ex.Message);
    }

    // Criterio 2
    [Fact]
    public async Task Vino_RegistraFechaSedeCarpetaAutorYHora_YRechazaElSegundoDelMismoDia()
    {
        var carpetaId = Sembrar(RutAna, Hoy, sede: Sede.MercadoPuerto);

        var dto = await _db.NewAsistenciaService().MarcarVinoAsync("12.345.678-5", "Funcionario Uno");

        Assert.Equal(TipoAsistencia.Vino, dto.Tipo);
        using var ctx = _db.NewContext();
        var fila = Assert.Single(ctx.Asistencias.AsNoTracking().ToList());
        Assert.Equal(Hoy, fila.Fecha);
        Assert.Equal(Sede.MercadoPuerto, fila.Sede);
        Assert.Equal(carpetaId, fila.CarpetaId);
        Assert.Equal("Funcionario Uno", fila.Autor);
        Assert.Equal("12345678-5", fila.Rut);
        Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, fila.RegistradaEn);
        Assert.False(fila.Anulada);

        await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "Funcionario Dos"));
        using var ctx2 = _db.NewContext();
        Assert.Equal(1, ctx2.Asistencias.Count());
    }

    [Fact]
    public async Task Vino_SinCitaDeHoy_SeRechaza()
    {
        Sembrar(RutAna, new DateOnly(2025, 3, 3));

        var ex = await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "Funcionario Uno"));

        Assert.Contains("cita", ex.Message);
    }

    [Fact]
    public void IndiceUnico_ImpideDosAsistenciasVigentesDelMismoRutYDia_PeroPermiteLaAnuladaYLasPorAgregar()
    {
        var carpetaId = Sembrar(RutAna, Hoy);
        var rut = Rut.Parse(RutAna);
        var ahora = _db.Clock.GetUtcNow().UtcDateTime;

        using var ctx = _db.NewContext();
        var anulada = Asistencia.Vino(carpetaId, rut, Sede.Placilla, Hoy, "u", ahora);
        anulada.Anular("u", ahora);
        ctx.Asistencias.AddRange(
            anulada,
            Asistencia.PorAgregar(rut, Sede.Placilla, Hoy, "u", ahora),
            Asistencia.PorAgregar(rut, Sede.Placilla, Hoy, "u", ahora),
            Asistencia.Vino(carpetaId, rut, Sede.Placilla, Hoy, "u", ahora));
        ctx.SaveChanges();

        using var ctx2 = _db.NewContext();
        ctx2.Asistencias.Add(Asistencia.Extra(carpetaId, rut, Sede.Placilla, Hoy, "u", ahora));
        Assert.Throws<DbUpdateException>(() => ctx2.SaveChanges());
    }

    // Criterio 3
    [Fact]
    public async Task Extra_RutNuevo_CreaCiudadanoYCarpetaPorClasificarConEtiquetaExtra_FueraDeSinTermino()
    {
        var dto = await _db.NewAsistenciaService().AgregarExtraAsync(Extra("22.222.222-2", "Persona Ejemplo Soto", "Renovación"));

        Assert.Equal(TipoAsistencia.Extra, dto.Tipo);
        using var ctx = _db.NewContext();
        var carpeta = ctx.Carpetas.Include(c => c.Ciudadano).Include(c => c.Historial).Single();
        Assert.True(carpeta.EsExtra);
        Assert.Equal(EstadoCarpeta.PorClasificar, carpeta.Estado);
        Assert.Equal(Bandeja.PorClasificar, carpeta.Bandeja);
        Assert.Equal("Renovación", carpeta.TipoTramite);
        Assert.Equal(TramiteAgenda.Renovacion, carpeta.TramiteAgenda);
        Assert.Equal(Hoy, carpeta.FechaCitacion);
        Assert.Equal("22222222-2", carpeta.Ciudadano.Rut.Value);
        Assert.Equal("PERSONA EJEMPLO SOTO", carpeta.Ciudadano.NombreCompleto.ToUpperInvariant());
        var fila = Assert.Single(ctx.Asistencias.ToList());
        Assert.Equal(TipoAsistencia.Extra, fila.Tipo);
        Assert.Equal(carpeta.Id, fila.CarpetaId);
        Assert.Contains(carpeta.Historial, h => h.Campo == "Extra");
    }

    [Fact]
    public async Task Extra_TramiteLibre_QuedaEnTipoTramiteSinTramiteAgenda()
    {
        await _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "Persona Ejemplo Soto", "Duplicado"));

        using var ctx = _db.NewContext();
        var carpeta = ctx.Carpetas.Single();
        Assert.Equal("Duplicado", carpeta.TipoTramite);
        Assert.Null(carpeta.TramiteAgenda);
    }

    [Fact]
    public async Task Extra_CiudadanoExistenteSinCitaHoy_ReutilizaCiudadanoYNoNecesitaNombre()
    {
        // Only an archived carpeta: an open one would make the extra a duplicate (see the next test).
        Sembrar(RutAna, new DateOnly(2025, 3, 3));
        using (var ctx0 = _db.NewContext())
        {
            ctx0.Carpetas.Single().CambiarDecision(Decision.Otorgado, "imp", Ayer);
            ctx0.SaveChanges();
        }

        await _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutAna));

        using var ctx = _db.NewContext();
        Assert.Equal(1, ctx.Ciudadanos.Count());
        Assert.Equal(2, ctx.Carpetas.Count());
    }

    [Fact]
    public async Task Buscar_IndicaSiLaPersonaTieneCarpetaAbierta()
    {
        Assert.False((await _db.NewAsistenciaService().BuscarAsync(RutNuevo)).TieneCarpetaAbierta);
        Sembrar(RutAna, new DateOnly(2025, 3, 3));

        Assert.True((await _db.NewAsistenciaService().BuscarAsync(RutAna)).TieneCarpetaAbierta);
    }

    [Fact]
    public async Task Extra_RutConCarpetaAbierta_SeBloqueaSinDuplicar_AunqueHayaPasadoPorDejarParaDespues()
    {
        Sembrar(RutAna, new DateOnly(2025, 3, 3));
        await _db.NewAsistenciaService().DejarParaDespuesAsync(RutAna, Sede.Placilla, "Funcionario Uno");

        var ex = await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutAna)));

        Assert.Contains("carpeta abierta", ex.Message);
        using var ctx = _db.NewContext();
        Assert.Equal(1, ctx.Carpetas.Count());
        Assert.False(ctx.Asistencias.Single(a => a.Tipo == TipoAsistencia.PorAgregar).Anulada);
    }

    [Fact]
    public async Task Extra_RutNuevoSinNombre_SeRechazaYNoGuardaNada()
    {
        await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "  ")));

        using var ctx = _db.NewContext();
        Assert.Equal(0, ctx.Ciudadanos.Count());
        Assert.Equal(0, ctx.Asistencias.Count());
    }

    [Fact]
    public async Task Extra_ConCitaDeHoy_SeRechazaPorqueDebeMarcarseVino()
    {
        Sembrar(RutAna, Hoy);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutAna)));

        Assert.Contains("Vino", ex.Message);
    }

    [Fact]
    public async Task Extra_DosVecesElMismoDia_SeRechaza()
    {
        await _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "Persona Ejemplo Soto"));

        await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "Persona Ejemplo Soto")));
        using var ctx = _db.NewContext();
        Assert.Equal(1, ctx.Carpetas.Count());
    }

    // Criterio 4
    [Fact]
    public async Task DejarParaDespues_CreaPorAgregar_YAlResolverComoExtraSeAnulaSinBorrar()
    {
        var pendiente = await _db.NewAsistenciaService().DejarParaDespuesAsync(RutNuevo, Sede.Placilla, "Funcionario Uno");
        Assert.Equal(TipoAsistencia.PorAgregar, pendiente.Tipo);
        Assert.Single((await _db.NewAsistenciaService().ObtenerResumenAsync()).PorAgregar);

        await _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "Persona Ejemplo Soto", autor: "Funcionario Dos"));

        using var ctx = _db.NewContext();
        var filas = ctx.Asistencias.AsNoTracking().ToList();
        Assert.Equal(2, filas.Count);
        var vieja = filas.Single(a => a.Tipo == TipoAsistencia.PorAgregar);
        Assert.True(vieja.Anulada);
        Assert.Equal("Funcionario Dos", vieja.AnuladaPor);
        Assert.NotNull(vieja.AnuladaEn);
        Assert.False(filas.Single(a => a.Tipo == TipoAsistencia.Extra).Anulada);
        Assert.Empty((await _db.NewAsistenciaService().ObtenerResumenAsync()).PorAgregar);
    }

    [Fact]
    public async Task DejarParaDespues_AlResolverComoVino_SeAnulaSinBorrar()
    {
        Sembrar(RutAna, Hoy);
        await _db.NewAsistenciaService().DejarParaDespuesAsync(RutAna, Sede.Placilla, "Funcionario Uno");

        await _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "Funcionario Uno");

        using var ctx = _db.NewContext();
        Assert.True(ctx.Asistencias.Single(a => a.Tipo == TipoAsistencia.PorAgregar).Anulada);
        Assert.False(ctx.Asistencias.Single(a => a.Tipo == TipoAsistencia.Vino).Anulada);
    }

    [Fact]
    public async Task DejarParaDespues_YaEnLaListaOYaMarcado_SeRechaza()
    {
        Sembrar(RutAna, Hoy);
        await _db.NewAsistenciaService().DejarParaDespuesAsync(RutNuevo, Sede.Placilla, "u");
        await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().DejarParaDespuesAsync(RutNuevo, Sede.Placilla, "u"));

        await _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "u");
        await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().DejarParaDespuesAsync(RutAna, Sede.Placilla, "u"));
    }

    [Fact]
    public async Task Buscar_ReflejaMarcadaYPorAgregarVigentes()
    {
        Sembrar(RutAna, Hoy);
        await _db.NewAsistenciaService().DejarParaDespuesAsync(RutAna, Sede.Placilla, "u");
        var antes = await _db.NewAsistenciaService().BuscarAsync(RutAna);
        Assert.NotNull(antes.PorAgregar);
        Assert.Null(antes.Marcada);

        await _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "u");
        var despues = await _db.NewAsistenciaService().BuscarAsync(RutAna);

        Assert.Equal(TipoAsistencia.Vino, despues.Marcada!.Tipo);
        Assert.Null(despues.PorAgregar);
    }

    // Criterio 5
    [Fact]
    public async Task Contadores_AgendadasVinieronYExtras_YDeshacerAnulaYDescuenta()
    {
        Sembrar(RutAna, Hoy);
        Sembrar(RutBruno, Hoy, "Bruno", "Muestra");
        Sembrar("9876543-3", new DateOnly(2026, 5, 5), "Otro", "Dia");
        var anulada = Sembrar("7654321-6", Hoy, "Anulada", "Demo");
        using (var ctx = _db.NewContext())
        {
            ctx.Carpetas.IgnoreQueryFilters().Single(c => c.Id == anulada).Anular("Duplicado", "u", Ayer);
            ctx.SaveChanges();
        }

        await _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "u");
        _db.AdvanceClock(TimeSpan.FromMinutes(5));
        var extra = await _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "Persona Ejemplo Soto"));
        var resumen = await _db.NewAsistenciaService().ObtenerResumenAsync();

        Assert.Equal(2, resumen.Contadores.AgendadasHoy);
        Assert.Equal(1, resumen.Contadores.Vinieron);
        Assert.Equal(1, resumen.Contadores.Extras);
        Assert.Equal(2, resumen.Marcados.Count);
        Assert.Equal(TipoAsistencia.Extra, resumen.Marcados[0].Tipo); // la más reciente primero
        Assert.Equal("22.222.222-2", resumen.Marcados[0].Rut);
        Assert.Equal("PERSONA EJEMPLO SOTO", resumen.Marcados[0].Nombre);

        await _db.NewAsistenciaService().DeshacerAsync(extra.Id, "Funcionario Dos");
        var vino = resumen.Marcados.Single(m => m.Tipo == TipoAsistencia.Vino);
        await _db.NewAsistenciaService().DeshacerAsync(vino.Id, "Funcionario Dos");
        var tras = await _db.NewAsistenciaService().ObtenerResumenAsync();

        Assert.Equal(2, tras.Contadores.AgendadasHoy);
        Assert.Equal(0, tras.Contadores.Vinieron);
        Assert.Equal(0, tras.Contadores.Extras);
        Assert.Empty(tras.Marcados);
        using var ctx2 = _db.NewContext();
        var filas = ctx2.Asistencias.AsNoTracking().ToList();
        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => { Assert.True(f.Anulada); Assert.Equal("Funcionario Dos", f.AnuladaPor); });
        var carpetaExtra = filas.Single(f => f.Tipo == TipoAsistencia.Extra).CarpetaId;
        Assert.True(ctx2.Carpetas.IgnoreQueryFilters().Single(c => c.Id == carpetaExtra).Anulada);
        // Deshacer un Vino permite volver a marcarlo.
        await _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "u");
    }

    [Fact]
    public async Task Contadores_LasExtrasNoCuentanComoAgendadas()
    {
        await _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "Persona Ejemplo Soto"));

        var r = await _db.NewAsistenciaService().ObtenerResumenAsync();

        Assert.Equal(0, r.Contadores.AgendadasHoy);
        Assert.Equal(1, r.Contadores.Extras);
    }

    [Fact]
    public async Task Deshacer_ExtraYaClasificada_SeRechazaYNoAnulaNada()
    {
        var extra = await _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "Persona Ejemplo Soto"));
        using (var ctx = _db.NewContext())
        {
            ctx.Carpetas.Single().CambiarEstado(EstadoCarpeta.PrimeraLicencia, "u", Ayer);
            ctx.SaveChanges();
        }

        await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().DeshacerAsync(extra.Id, "u"));

        using var ctx2 = _db.NewContext();
        Assert.False(ctx2.Asistencias.Single().Anulada);
    }

    [Fact]
    public async Task Deshacer_DosVeces_SeRechazaLaSegunda()
    {
        Sembrar(RutAna, Hoy);
        var vino = await _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "u");
        await _db.NewAsistenciaService().DeshacerAsync(vino.Id, "u");

        await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().DeshacerAsync(vino.Id, "u"));
    }

    // Criterio 3 (etiqueta en Por clasificar) y menú
    [Fact]
    public async Task PorClasificar_LaListaMarcaLaExtraConEsExtra_YLaAgendadaNo()
    {
        Sembrar(RutAna, Hoy);
        await _db.NewAsistenciaService().AgregarExtraAsync(Extra(RutNuevo, "Persona Ejemplo Soto"));

        var lista = await _db.NewService().ListarAsync(new ListarCarpetasQuery(Bandeja: Bandeja.PorClasificar));

        Assert.Equal(2, lista.Items.Count);
        Assert.True(lista.Items.Single(i => i.Rut == "22.222.222-2").EsExtra);
        Assert.False(lista.Items.Single(i => i.Rut == "12.345.678-5").EsExtra);
    }

    [Fact]
    public void Menu_TieneAsistenciaDelDiaEnElGrupoDeEntrada()
    {
        var modulo = Assert.Single(Sgl.Web.Services.ModulosCatalogo.Todos, m => m.Clave == Sgl.Web.Services.ModulosCatalogo.ClaveAsistencia);

        Assert.Equal("Asistencia del día", modulo.Nombre);
        Assert.Equal("asistencia", modulo.Href);
        Assert.Same(Sgl.Web.Services.ModulosCatalogo.Entrada, modulo.Grupo);
    }

    [Fact]
    public async Task AutorVacio_SeRechazaAlMarcar()
    {
        Sembrar(RutAna, Hoy);

        await Assert.ThrowsAsync<DomainException>(() => _db.NewAsistenciaService().MarcarVinoAsync(RutAna, "  "));
    }
}
