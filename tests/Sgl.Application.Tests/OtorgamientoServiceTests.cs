using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sgl.Application.Carpetas;
using Sgl.Application.Otorgamiento;
using Sgl.Application.Seguridad;
using Sgl.Domain;
using Sgl.Web.Services;

namespace Sgl.Application.Tests;

/// <summary>Tramo 6: módulo «Otorgamiento» (bandeja «Para otorgar», decisiones y permiso «puede otorgar»).</summary>
public sealed class OtorgamientoServiceTests : IDisposable
{
    private static readonly DateTime Antes = new(2026, 5, 3, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = new();
    private int _rut = 11111111;

    public void Dispose() => _db.Dispose();

    private sealed class Permiso(bool permitido) : IPermisos
    {
        public bool PuedeOtorgar() => permitido;
    }

    private OtorgamientoService Servicio(bool permitido = true) => _db.NewOtorgamientoService(new Permiso(permitido));

    private static string RutValido(int cuerpo)
    {
        var suma = 0;
        var factor = 2;
        for (var n = cuerpo; n > 0; n /= 10)
        {
            suma += n % 10 * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }
        var dv = 11 - suma % 11;
        return $"{cuerpo}-{(dv == 11 ? "0" : dv == 10 ? "K" : dv.ToString())}";
    }

    private async Task<Guid> SembrarAsync(
        EstadoCarpeta estado, Sede sede = Sede.AvArgentina, DateOnly? subida = null, string? caja = null, bool anular = false, Decision decision = Decision.Pendiente)
    {
        await using var ctx = _db.NewContext();
        var persona = new Ciudadano(Rut.Parse(RutValido(_rut++)), "Persona", "Ficticia");
        // Una caja con la carpeta aún pendiente solo existe como dato heredado (importado): una asignación nueva exige decisión final.
        var c = caja is null
            ? Carpeta.Registrar(persona, sede, new DateOnly(2026, 4, 1), "setup", Antes)
            : Carpeta.CrearImportada(Guid.NewGuid(), persona, sede, new DateOnly(2026, 4, 1), null, null, EstadoCarpeta.Citada, Decision.Pendiente, null, null, caja, null, Antes);
        c.ForzarEstado(estado, "setup", Antes, SentidoCambioDomicilio.Saliente);
        if (subida is not null) c.Editar(sede, new DateOnly(2026, 4, 1), subida, null, "setup", Antes);
        if (decision == Decision.Otorgado) c.CambiarDecision(Decision.Otorgado, "setup", Antes);
        if (anular) c.Anular("Duplicado", "setup", Antes);
        ctx.Carpetas.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<CarpetaDetalleDto> DetalleAsync(Guid id) => await _db.NewService().ObtenerAsync(id);

    // Criterio 3
    [Fact]
    public async Task Para_otorgar_lista_solo_abiertas_pendientes_en_los_estados_que_corresponden()
    {
        var subida = await SembrarAsync(EstadoCarpeta.SubidaConaset, subida: new DateOnly(2026, 4, 10));
        var f8 = await SembrarAsync(EstadoCarpeta.SubidaConF8, Sede.Placilla, new DateOnly(2026, 4, 12));
        var oficio = await SembrarAsync(EstadoCarpeta.SubidaConOficio);
        var domConaset = await SembrarAsync(EstadoCarpeta.CambioDomSubidoConaset);
        var domCorreo = await SembrarAsync(EstadoCarpeta.CambioDomSubidoCorreo);
        var canje = await SembrarAsync(EstadoCarpeta.CanjeLicExtranjera);
        var primera = await SembrarAsync(EstadoCarpeta.PrimeraLicencia);
        var enCola = await SembrarAsync(EstadoCarpeta.SubidaConaset, caja: Carpeta.ColaDeCaja);

        // Fuera de la bandeja
        await SembrarAsync(EstadoCarpeta.Citada);
        await SembrarAsync(EstadoCarpeta.CambioDomicilio);
        await SembrarAsync(EstadoCarpeta.SeEncuentraEnArchivos);
        await SembrarAsync(EstadoCarpeta.CrearCertificado);
        await SembrarAsync(EstadoCarpeta.SubidaConaset, decision: Decision.Otorgado);
        await SembrarAsync(EstadoCarpeta.SubidaConaset, anular: true);
        await SembrarAsync(EstadoCarpeta.SubidaConaset, caja: "A-12");

        var r = await Servicio().ListarAsync(new FiltroParaOtorgar(), 100);

        var esperados = new[] { subida, f8, oficio, domConaset, domCorreo, canje, primera, enCola }.Order().ToList();
        Assert.Equal(esperados, r.Items.Select(i => i.Id).Order().ToList());
        Assert.Equal(8, r.Total);
        Assert.Equal(8, await Servicio().ContarAsync());
    }

    [Fact]
    public async Task Bandeja_muestra_solicitudes_y_respaldo_pendientes_sin_bloquear_decision()
    {
        var id = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        using (var ctx = _db.NewContext())
        {
            ctx.SolicitudesF8.Add(SolicitudF8.Solicitar(id, EstadoCarpeta.NoExisteCarpeta, "ana", Antes));
            ctx.SolicitudesCertificado.Add(SolicitudCertificado.CrearManual(id, TipoCertificado.RegistroCivil, null, null, "ana", Antes));
            ctx.Carpetas.Single(c => c.Id == id).AgregarRespaldoPenultima("ana", Antes);
            await ctx.SaveChangesAsync();
        }
        var fila = (await Servicio().ListarAsync(new FiltroParaOtorgar(), 100)).Items.Single();
        Assert.True(fila.F8Pendiente);
        Assert.True(fila.CertificadoPendiente);
        Assert.True(fila.RespaldoPendiente);
        await Servicio().OtorgarAsync(id, "ana");
        Assert.Equal(Decision.Otorgado, (await DetalleAsync(id)).Decision);
    }

    [Fact]
    public async Task Para_otorgar_filtra_por_sede_y_por_fecha_de_subida_y_limita_filas_con_el_total_real()
    {
        var a = await SembrarAsync(EstadoCarpeta.SubidaConaset, Sede.AvArgentina, new DateOnly(2026, 4, 10));
        var b = await SembrarAsync(EstadoCarpeta.SubidaConaset, Sede.Placilla, new DateOnly(2026, 4, 20));
        var c = await SembrarAsync(EstadoCarpeta.SubidaConaset, Sede.Placilla, new DateOnly(2026, 4, 30));

        var porSede = await Servicio().ListarAsync(new FiltroParaOtorgar(Sede.Placilla), 100);
        Assert.Equal([b, c], porSede.Items.Select(i => i.Id).ToList());

        var porFecha = await Servicio().ListarAsync(new FiltroParaOtorgar(null, new DateOnly(2026, 4, 15), new DateOnly(2026, 4, 25)), 100);
        Assert.Equal([b], porFecha.Items.Select(i => i.Id).ToList());

        var limitada = await Servicio().ListarAsync(new FiltroParaOtorgar(), 2);
        Assert.Equal(2, limitada.Items.Count);
        Assert.Equal(3, limitada.Total);
        Assert.Equal(a, limitada.Items[0].Id);
    }

    [Theory]
    [InlineData(EstadoCarpeta.EsperaExamen, Resolucion.EsperaExamen)]
    [InlineData(EstadoCarpeta.ClasePendiente, Resolucion.ClasePendiente)]
    [InlineData(EstadoCarpeta.ParaDenegar, Resolucion.ParaDenegar)]
    public async Task Las_legadas_aparecen_con_la_resolucion_derivada_y_su_dato_no_se_modifica(EstadoCarpeta estado, Resolucion esperada)
    {
        var id = await SembrarAsync(estado);
        var historialAntes = (await DetalleAsync(id)).Historial.Count;

        var fila = (await Servicio().ListarAsync(new FiltroParaOtorgar(), 100)).Items.Single(i => i.Id == id);

        Assert.Equal(esperada, fila.Resolucion);
        var detalle = await DetalleAsync(id);
        Assert.Equal(estado, detalle.Estado);
        Assert.Equal(Decision.Pendiente, detalle.Decision);
        Assert.Equal(historialAntes, detalle.Historial.Count);
        await using var ctx = _db.NewContext();
        Assert.Null(await ctx.Carpetas.Where(c => c.Id == id).Select(c => c.Resolucion).SingleAsync());
    }

    [Fact]
    public async Task Otorgada_estampa_la_fecha_de_hoy_Chile_y_la_carpeta_pasa_a_Cerradas()
    {
        var id = await SembrarAsync(EstadoCarpeta.SubidaConaset, subida: new DateOnly(2026, 4, 10));

        await Servicio().OtorgarAsync(id, "ana");

        var d = await DetalleAsync(id);
        Assert.Equal(Decision.Otorgado, d.Decision);
        await using var ctx = _db.NewContext();
        Assert.Equal(_db.Clock.HoyChile(), await ctx.Carpetas.Where(c => c.Id == id).Select(c => c.FechaOtorgamiento).SingleAsync());
        var cerradas = await _db.NewService().ListarAsync(new ListarCarpetasQuery { Bandeja = Bandeja.Cerradas });
        Assert.Contains(cerradas.Items, i => i.Id == id);
        Assert.Empty((await Servicio().ListarAsync(new FiltroParaOtorgar(), 100)).Items);
        Assert.Equal(1, (await _db.NewService().ObtenerEstadisticasAsync()).TotalOtorgadas);
    }

    [Fact]
    public async Task Denegada_y_las_tres_resoluciones_quedan_en_el_historial_y_la_resolucion_se_edita()
    {
        var deniega = await SembrarAsync(EstadoCarpeta.SubidaConF8);
        var resuelve = await SembrarAsync(EstadoCarpeta.PrimeraLicencia);

        await Servicio().DenegarAsync(deniega, "ana");
        await Servicio().AsignarResolucionAsync(resuelve, Resolucion.EsperaExamen, "ana");
        await Servicio().AsignarResolucionAsync(resuelve, Resolucion.ClasePendiente, "luis");

        Assert.Equal(Decision.Denegado, (await DetalleAsync(deniega)).Decision);
        var fila = (await Servicio().ListarAsync(new FiltroParaOtorgar(), 100)).Items.Single(i => i.Id == resuelve);
        Assert.Equal(Resolucion.ClasePendiente, fila.Resolucion);
        var historial = (await DetalleAsync(resuelve)).Historial;
        Assert.Contains(historial, h => h.Campo == "Resolucion" && h.Anterior == "EsperaExamen" && h.Nuevo == "ClasePendiente" && h.Usuario == "luis");
    }

    // Criterio 4
    [Fact]
    public async Task Sin_permiso_lanza_No_tiene_permiso_para_otorgar_y_no_guarda_nada()
    {
        var id = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var historialAntes = (await DetalleAsync(id)).Historial.Count;
        var sin = Servicio(permitido: false);

        var e1 = await Assert.ThrowsAsync<DomainException>(() => sin.OtorgarAsync(id, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => sin.DenegarAsync(id, "ana"));
        await Assert.ThrowsAsync<DomainException>(() => sin.AsignarResolucionAsync(id, Resolucion.EsperaExamen, "ana"));

        Assert.Equal("No tiene permiso para otorgar.", e1.Message);
        var d = await DetalleAsync(id);
        Assert.Equal(Decision.Pendiente, d.Decision);
        Assert.Equal(historialAntes, d.Historial.Count);
        await using var ctx = _db.NewContext();
        Assert.Null(await ctx.Carpetas.Where(c => c.Id == id).Select(c => c.FechaOtorgamiento).SingleAsync());
    }

    [Fact]
    public async Task Una_carpeta_no_apta_se_rechaza_y_no_guarda()
    {
        var id = await SembrarAsync(EstadoCarpeta.Citada);

        await Assert.ThrowsAsync<DomainException>(() => Servicio().OtorgarAsync(id, "ana"));

        Assert.Equal(Decision.Pendiente, (await DetalleAsync(id)).Decision);
    }

    // Criterio 6
    [Fact]
    public async Task Deshacer_una_Otorgada_devuelve_Pendiente_y_quita_la_fecha_de_otorgamiento()
    {
        var id = await SembrarAsync(EstadoCarpeta.PrimeraLicencia, subida: new DateOnly(2026, 4, 10));
        var punto = await Servicio().OtorgarAsync(id, "ana");

        await Servicio().DeshacerAsync(id, punto, "ana");

        var d = await DetalleAsync(id);
        Assert.Equal(Decision.Pendiente, d.Decision);
        Assert.Null(d.FechaCierre);
        await using var ctx = _db.NewContext();
        Assert.Null(await ctx.Carpetas.Where(c => c.Id == id).Select(c => c.FechaOtorgamiento).SingleAsync());
        Assert.Contains(id, (await Servicio().ListarAsync(new FiltroParaOtorgar(), 100)).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Deshacer_tambien_exige_permiso_y_se_rechaza_si_el_estado_cambio()
    {
        var id = await SembrarAsync(EstadoCarpeta.SubidaConaset);
        var punto = await Servicio().OtorgarAsync(id, "ana");

        await Assert.ThrowsAsync<DomainException>(() => Servicio(permitido: false).DeshacerAsync(id, punto, "ana"));
        Assert.Equal(Decision.Otorgado, (await DetalleAsync(id)).Decision);

        var otroEstado = punto with { Estado = EstadoCarpeta.SubidaConF8 };
        await Assert.ThrowsAsync<DomainException>(() => Servicio().DeshacerAsync(id, otroEstado, "ana"));
        Assert.Equal(Decision.Otorgado, (await DetalleAsync(id)).Decision);
    }

    // Criterio 5
    private sealed class LoggerContador : ILogger<PermisosPorConfiguracion>
    {
        public List<string> Advertencias { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Advertencias.Add(formatter(state, exception));
        }
    }

    [Fact]
    public void Lista_vacia_permite_y_registra_una_sola_advertencia_sin_nombres()
    {
        var opciones = new OtorgadoresOptions([]);
        var log = new LoggerContador();
        var autor = new AutorState { Nombre = "Pedro Inventado" };

        var p1 = new PermisosPorConfiguracion(opciones, autor, log);
        var p2 = new PermisosPorConfiguracion(opciones, new AutorState(), log);

        Assert.True(p1.PuedeOtorgar());
        Assert.True(p1.PuedeOtorgar());
        Assert.True(p2.PuedeOtorgar());
        var unica = Assert.Single(log.Advertencias);
        Assert.Contains("Seguridad:Otorgadores", unica);
        Assert.DoesNotContain("Pedro", unica);
    }

    [Fact]
    public void Con_lista_llena_compara_sin_distinguir_mayusculas_y_rechaza_a_los_demas()
    {
        var opciones = new OtorgadoresOptions(["Maria Inventada", "dominio\\Juan.Ficticio"]);
        var log = new LoggerContador();

        Assert.True(new PermisosPorConfiguracion(opciones, new AutorState { Nombre = "MARIA INVENTADA" }, log).PuedeOtorgar());
        Assert.True(new PermisosPorConfiguracion(opciones, new AutorState { Nombre = "dominio\\juan.ficticio" }, log).PuedeOtorgar());
        Assert.False(new PermisosPorConfiguracion(opciones, new AutorState { Nombre = "Otra Persona" }, log).PuedeOtorgar());
        Assert.False(new PermisosPorConfiguracion(opciones, new AutorState(), log).PuedeOtorgar());
        Assert.Empty(log.Advertencias);
    }

    [Fact]
    public void Un_login_de_Windows_coincide_por_su_parte_de_usuario()
    {
        var opciones = new OtorgadoresOptions(["Maria.Inventada"]);

        var autor = new AutorState();
        autor.FijarAutenticado("MUNI\\maria.inventada");

        Assert.True(new PermisosPorConfiguracion(opciones, autor, new LoggerContador()).PuedeOtorgar());
    }

    [FactConRepositorio]
    public void Ni_el_codigo_ni_la_configuracion_por_defecto_traen_nombres_de_otorgadores()
    {
        var raiz = RaizRepositorio.Buscar()!;

        var json = File.ReadAllText(Path.Combine(raiz, "src", "Sgl.Web", "appsettings.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var lista = doc.RootElement.GetProperty("Seguridad").GetProperty("Otorgadores");
        Assert.Equal(0, lista.GetArrayLength());

        // Nor may the code carry a hardcoded list: OtorgadoresOptions is only ever built from configuration.
        var listaEnCodigo = new System.Text.RegularExpressions.Regex(
            @"OtorgadoresOptions\s*\(\s*(\[|new\b)|Otorgadores\s*=\s*(\[|new\b)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        var sospechosos = Directory.EnumerateFiles(Path.Combine(raiz, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => listaEnCodigo.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(raiz, f))
            .ToList();
        Assert.Empty(sospechosos);
    }
}
