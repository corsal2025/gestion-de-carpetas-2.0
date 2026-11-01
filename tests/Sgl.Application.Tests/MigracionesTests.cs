using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sgl.Infrastructure;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

/// <summary>H-20: las migraciones reflejan el modelo (sin el ignore de PendingModelChangesWarning) y crean los índices.</summary>
public sealed class MigracionesTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"sgl-migra-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var sufijo in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_ruta + sufijo); } catch { /* aún tomado por el proceso */ }
        }
    }

    [Fact]
    public void Identidad_canceladas_preserva_estadistica_heredada_y_permite_distintas_reservas()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>().UseSqlite($"Data Source={_ruta}").Options;
        using var ctx = new SglDbContext(options);
        ctx.GetService<IMigrator>().Migrate("20261101130000_ClasesAgenda");
        ctx.Database.ExecuteSqlRaw("INSERT INTO CitasCanceladas (FechaCita,FechaTomaCita,HoraCita,Sede,Tramite,RegistradaEn) VALUES ('2026-12-30','2026-06-16 11:03:00','12:00:00','Placilla','Renovación','2026-11-01 10:00:00')");
        ctx.Database.Migrate();
        Assert.Equal(1, ctx.CitasCanceladas.Count());
        Assert.Null(ctx.CitasCanceladas.Single().IdentidadAgenda);
        var a = new Sgl.Domain.CitaCancelada(new DateOnly(2026,12,30),new DateTime(2026,6,16,11,3,0),new TimeOnly(12,0),Sgl.Domain.Sede.Placilla,"Renovación",null,DateTime.UtcNow);
        a.VincularIdentidad(new string('A',64));
        ctx.Add(a); ctx.SaveChanges();
        Assert.Equal(2, ctx.CitasCanceladas.Count());
    }

    [Fact]
    public void Respaldo_migracion_preserva_las_carpetas_y_no_crea_pendientes_para_registros_anteriores()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);
        ctx.GetService<IMigrator>().Migrate("20261101025143_ReclasificacionCambioDom");
        var ciudadano = InsertarFilaSintetica(ctx, "Ciudadanos", null, null);
        var id = InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 0);
        var antes = ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado AS Value FROM Carpetas").Single();
        ctx.Database.Migrate();
        Assert.Equal(antes, ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado AS Value FROM Carpetas").Single());
        Assert.Equal(1, ctx.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Carpetas WHERE Id = {id} AND FechaRespaldoSolicitado IS NULL AND FechaRespaldoSubido IS NULL AND ClasesAgenda IS NULL").Single());
    }

    [Fact]
    public void Migrate_AplicaTodoSinCambiosPendientesDelModelo_YCreaLosIndicesNuevos()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);

        ctx.Database.Migrate(); // lanza si el modelo tiene cambios sin migración

        var indices = ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToList();
        Assert.Contains("IX_Carpetas_FechaSubida", indices);
        Assert.Contains("IX_HistorialCambios_Campo_CarpetaId_Fecha", indices);
        Assert.Contains("IX_Carpetas_CiudadanoId_FechaTomaCita", indices);
        Assert.Contains("IX_CitasCanceladas_FechaTomaCita_FechaCita_HoraCita_Sede_Tramite", indices);
        Assert.Contains("IX_Asistencias_Rut_Fecha", indices);
        Assert.Contains("IX_Asistencias_Fecha_Tipo", indices);
        Assert.Contains("IX_Carpetas_Decision_Estado", indices);
        var unico = ctx.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE name = 'IX_Asistencias_Rut_Fecha'").Single();
        Assert.Contains("UNIQUE", unico);
        Assert.Contains("PorAgregar", unico);
    }

    [Fact]
    public void LupaTresPosiciones_AgregaCarpetaEncontradaEnFalse_ySinMoverLaMarcaDeBusqueda()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);
        var migrator = ctx.GetService<IMigrator>();

        migrator.Migrate("20261031034515_AsistenciaDelDia");
        Assert.DoesNotContain("CarpetaEncontrada", Columnas(ctx, "Carpetas"));

        var ciudadano = InsertarFilaSintetica(ctx, "Ciudadanos", null, null);
        InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 1);
        InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 1);
        InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 0);
        var antes = ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE PendienteBusqueda = 1").Single();

        ctx.Database.Migrate();

        Assert.Contains("CarpetaEncontrada", Columnas(ctx, "Carpetas"));
        Assert.Equal(antes, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE PendienteBusqueda = 1").Single());
        Assert.Equal(2, antes);
        Assert.Equal(0, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE CarpetaEncontrada <> 0").Single());
        Assert.Equal(3, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE CarpetaEncontrada = 0").Single());
    }

    [Fact]
    public void Otorgamiento_agrega_resolucion_y_fecha_nulas_y_no_toca_estado_ni_decision()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);
        var migrator = ctx.GetService<IMigrator>();

        migrator.Migrate("20261031111942_LupaTresPosiciones");
        Assert.DoesNotContain("Resolucion", Columnas(ctx, "Carpetas"));
        Assert.DoesNotContain("FechaOtorgamiento", Columnas(ctx, "Carpetas"));

        var ciudadano = InsertarFilaSintetica(ctx, "Ciudadanos", null, null);
        foreach (var (estado, decision) in new[] { ("EsperaExamen", "Pendiente"), ("SubidaConaset", "Otorgado"), ("SubidaConaset", "Pendiente"), ("PrimeraLicencia", "Denegado") })
        {
            var id = InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 0);
            ctx.Database.ExecuteSql($"UPDATE Carpetas SET Estado = {estado}, Decision = {decision} WHERE Id = {id}");
        }
        var antes = ctx.Database.SqlQueryRaw<string>("SELECT Estado || '/' || Decision AS Value FROM Carpetas ORDER BY Id").ToList();

        ctx.Database.Migrate();

        Assert.Contains("Resolucion", Columnas(ctx, "Carpetas"));
        Assert.Contains("FechaOtorgamiento", Columnas(ctx, "Carpetas"));
        Assert.Equal(antes, ctx.Database.SqlQueryRaw<string>("SELECT Estado || '/' || Decision AS Value FROM Carpetas ORDER BY Id").ToList());
        Assert.Equal(4, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE Resolucion IS NULL AND FechaOtorgamiento IS NULL").Single());
    }

    // E8 criterio 8
    [Fact]
    public void CambioDomicilioSentido_rellena_la_direccion_sin_cambiar_conteo_ni_estados()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);
        var migrator = ctx.GetService<IMigrator>();

        migrator.Migrate("20261031113334_Otorgamiento");
        Assert.DoesNotContain("SentidoCambioDomicilio", Columnas(ctx, "Carpetas"));

        var ciudadano = InsertarFilaSintetica(ctx, "Ciudadanos", null, null);
        var sembradas = new Dictionary<string, string>();
        foreach (var (clave, estado, deCorreo) in new[]
        {
            ("entrante-en-bandeja", "CambioDomicilio", true),
            ("entrante-ya-subida", "CambioDomSubidoConaset", true),
            ("entrante-con-f8", "SubidaConF8", true),
            ("heredada-sin-fila", "CambioDomicilio", false),
            ("solicitada", "CambioDomicilioSolicitado", false),
            ("subida-conaset", "CambioDomSubidoConaset", false),
            ("subida-correo", "CambioDomSubidoCorreo", false),
            ("primera", "PrimeraLicencia", false),
            ("clasificar", "PorClasificar", false),
            ("subida-normal", "SubidaConaset", false),
        })
        {
            var id = InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 0);
            ctx.Database.ExecuteSql($"UPDATE Carpetas SET Estado = {estado} WHERE Id = {id}");
            sembradas[clave] = id;
            if (deCorreo)
            {
                var minusculas = id.ToLowerInvariant();
                ctx.Database.ExecuteSql($"INSERT INTO CorreosCambioDomicilio (MessageId, Rut, CarpetaId, ProcesadoEn) VALUES ({"<" + clave + "@x>"}, {"18785387-7"}, {minusculas}, {"2026-10-01 00:00:00"})");
            }
        }
        var antes = ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado AS Value FROM Carpetas ORDER BY Id").ToList();

        ctx.Database.Migrate();

        Assert.Contains("SentidoCambioDomicilio", Columnas(ctx, "Carpetas"));
        Assert.Equal(antes, ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado AS Value FROM Carpetas ORDER BY Id").ToList());
        var sentidos = ctx.Database.SqlQueryRaw<string>("SELECT Id || '=' || COALESCE(SentidoCambioDomicilio, 'NULL') AS Value FROM Carpetas")
            .ToList().Select(x => x.Split('=')).ToDictionary(x => x[0], x => x[1]);
        string De(string clave) => sentidos[sembradas[clave]];
        Assert.Equal(["2", "2", "2"], new[] { De("entrante-en-bandeja"), De("entrante-ya-subida"), De("entrante-con-f8") });
        Assert.Equal(["1", "1", "1", "1"], new[] { De("heredada-sin-fila"), De("solicitada"), De("subida-conaset"), De("subida-correo") });
        Assert.Equal(["NULL", "NULL", "NULL"], new[] { De("primera"), De("clasificar"), De("subida-normal") });
        Assert.Contains("IX_Carpetas_SentidoCambioDomicilio_Estado", ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToList());
    }

    // E9
    [Fact]
    public void EnviosCorreoComuna_agrega_tablas_indices_y_RevisadaEn_sin_cambiar_conteo_ni_estados()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);
        var migrator = ctx.GetService<IMigrator>();

        migrator.Migrate("20261031163810_CambioDomicilioSentido");
        Assert.DoesNotContain("RevisadaEn", Columnas(ctx, "Carpetas"));
        Assert.Empty(ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE name IN ('EnviosCorreoComuna', 'EnviosCorreoComunaCarpetas', 'PlantillasCorreo')").ToList());

        var ciudadano = InsertarFilaSintetica(ctx, "Ciudadanos", null, null);
        foreach (var estado in new[] { "CambioDomicilio", "CambioDomicilioSolicitado", "CambioDomSubidoConaset", "PrimeraLicencia", "PorClasificar" })
        {
            var id = InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 0);
            ctx.Database.ExecuteSql($"UPDATE Carpetas SET Estado = {estado}, SentidoCambioDomicilio = 1 WHERE Id = {id}");
        }
        var antes = ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado || '/' || SentidoCambioDomicilio AS Value FROM Carpetas ORDER BY Id").ToList();

        ctx.Database.Migrate();

        Assert.Contains("RevisadaEn", Columnas(ctx, "Carpetas"));
        Assert.Equal(antes, ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado || '/' || SentidoCambioDomicilio AS Value FROM Carpetas ORDER BY Id").ToList());
        Assert.Equal(5, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE RevisadaEn IS NULL").Single());
        Assert.Equal(["Id", "Tipo", "Comuna", "Destinatarios", "Asunto", "Cuerpo", "Autor", "EnviadoEn", "EsPrueba", "Estado", "Error", "Nota", "PantallazoArchivo", "PantallazoEn"],
            Columnas(ctx, "EnviosCorreoComuna"));
        Assert.Equal(["EnvioId", "CarpetaId"], Columnas(ctx, "EnviosCorreoComunaCarpetas"));
        Assert.Equal(["Clave", "Asunto", "Cuerpo"], Columnas(ctx, "PlantillasCorreo"));
        var indices = ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToList();
        Assert.Contains("IX_EnviosCorreoComuna_Tipo_EnviadoEn", indices);
        Assert.Contains("IX_EnviosCorreoComuna_Estado", indices);
        Assert.Contains("IX_EnviosCorreoComunaCarpetas_CarpetaId", indices);
        var pk = ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('EnviosCorreoComunaCarpetas') WHERE pk > 0 ORDER BY pk").ToList();
        Assert.Equal(["EnvioId", "CarpetaId"], pk);
    }

    // E12 criterio 3: la migración guarda el RUT con que llegó la agenda, solo en las carpetas que tienen fecha de toma
    [Fact]
    public void RutImportacion_rellena_solo_las_carpetas_con_fecha_de_toma_sin_cambiar_conteo_ni_estados()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);
        var migrator = ctx.GetService<IMigrator>();

        migrator.Migrate("20261031165751_EnviosCorreoComuna");
        Assert.DoesNotContain("RutImportacion", Columnas(ctx, "Carpetas"));

        var ciudadano = InsertarFilaSintetica(ctx, "Ciudadanos", null, null);
        var rutCiudadano = ctx.Database.SqlQuery<string>($"SELECT Rut AS Value FROM Ciudadanos WHERE Id = {ciudadano}").Single();
        var deAgenda = new List<string>();
        var heredadas = new List<string>();
        foreach (var (estado, conToma) in new[] { ("PorClasificar", true), ("CambioDomicilio", true), ("SubidaConaset", false), ("PrimeraLicencia", false) })
        {
            var id = InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 0);
            ctx.Database.ExecuteSql($"UPDATE Carpetas SET Estado = {estado} WHERE Id = {id}");
            if (conToma)
            {
                ctx.Database.ExecuteSql($"UPDATE Carpetas SET FechaTomaCita = {"2026-10-29 11:03:00"} WHERE Id = {id}");
                deAgenda.Add(id);
            }
            else
            {
                heredadas.Add(id);
            }
        }
        var antes = ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado AS Value FROM Carpetas ORDER BY Id").ToList();

        ctx.Database.Migrate();

        Assert.Contains("RutImportacion", Columnas(ctx, "Carpetas"));
        Assert.Contains("IX_Carpetas_RutImportacion", ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToList());
        Assert.Equal(antes, ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado AS Value FROM Carpetas ORDER BY Id").ToList());
        Assert.Equal(2, ctx.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Carpetas WHERE RutImportacion = {rutCiudadano}").Single());
        Assert.Equal(2, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE RutImportacion IS NULL").Single());
        foreach (var id in deAgenda)
            Assert.Equal(rutCiudadano, ctx.Database.SqlQuery<string>($"SELECT RutImportacion AS Value FROM Carpetas WHERE Id = {id}").Single());
        foreach (var id in heredadas)
            Assert.Equal(0, ctx.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Carpetas WHERE Id = {id} AND RutImportacion IS NOT NULL").Single());
    }

    // E13: tabla aditiva de correos descartados; no toca carpetas ni estados y el MessageId es único
    [Fact]
    public void CorreosDescartados_agrega_la_tabla_unica_por_mensaje_sin_cambiar_conteo_ni_estados()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);
        var migrator = ctx.GetService<IMigrator>();

        migrator.Migrate("20261031180012_RutImportacion");
        Assert.Empty(ctx.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE name = 'CorreosDescartados'").ToList());

        var ciudadano = InsertarFilaSintetica(ctx, "Ciudadanos", null, null);
        foreach (var estado in new[] { "CambioDomicilio", "CambioDomSubidoConaset", "PorClasificar" })
        {
            var id = InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 0);
            ctx.Database.ExecuteSql($"UPDATE Carpetas SET Estado = {estado} WHERE Id = {id}");
        }
        var antes = ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado AS Value FROM Carpetas ORDER BY Id").ToList();

        ctx.Database.Migrate();

        Assert.Equal(["Id", "MessageId", "Asunto", "Remitente", "RecibidoEn", "Motivo", "DescartadoEn"], Columnas(ctx, "CorreosDescartados"));
        Assert.Equal(antes, ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado AS Value FROM Carpetas ORDER BY Id").ToList());
        var indice = ctx.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE name = 'IX_CorreosDescartados_MessageId'").Single();
        Assert.Contains("UNIQUE", indice);

        ctx.Database.ExecuteSql($"INSERT INTO CorreosDescartados (MessageId, Asunto, Remitente, RecibidoEn, Motivo, DescartadoEn) VALUES ({"<a@x>"}, {"a"}, {"r"}, {"2026-10-31 10:00:00"}, {"m"}, {"2026-10-31 10:00:00"})");
        Assert.ThrowsAny<Exception>(() =>
            ctx.Database.ExecuteSql($"INSERT INTO CorreosDescartados (MessageId, Asunto, Remitente, RecibidoEn, Motivo, DescartadoEn) VALUES ({"<a@x>"}, {"b"}, {"r"}, {"2026-10-31 10:00:00"}, {"m"}, {"2026-10-31 10:00:00"})"));
    }

    // Reclasificación criterio 8: ninguna carpeta existente retrocede a la etapa nueva.
    [Fact]
    public async Task ReclasificacionCambioDom_deja_todas_las_filas_previas_en_false_y_no_cambia_los_conteos()
    {
        var options = new DbContextOptionsBuilder<SglDbContext>();
        DatabaseProvider.Configure(options, DatabaseProvider.Sqlite, $"Data Source={_ruta}");
        using var ctx = new SglDbContext(options.Options);
        var migrator = ctx.GetService<IMigrator>();

        migrator.Migrate("20261031181731_CorreosDescartados");
        Assert.DoesNotContain("PendienteReclasificacion", Columnas(ctx, "Carpetas"));

        var ciudadano = InsertarFilaSintetica(ctx, "Ciudadanos", null, null);
        foreach (var (estado, sentido) in new (string, int?)[]
        {
            ("CambioDomicilio", 1), ("CambioDomicilio", 1), ("CambioDomicilio", 2),
            ("CambioDomicilioSolicitado", 1), ("CambioDomicilioSolicitado", 1), ("PrimeraLicencia", null), ("PorClasificar", null),
        })
        {
            var id = InsertarFilaSintetica(ctx, "Carpetas", ciudadano, 0);
            ctx.Database.ExecuteSql($"UPDATE Carpetas SET Estado = {estado}, Decision = {"Pendiente"}, SentidoCambioDomicilio = {sentido} WHERE Id = {id}");
        }
        var porPedirAntes = ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE Estado = 'CambioDomicilio' AND SentidoCambioDomicilio = 1").Single();
        var solicitadoAntes = ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE Estado = 'CambioDomicilioSolicitado'").Single();
        var entrantesAntes = ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE Estado = 'CambioDomicilio' AND SentidoCambioDomicilio = 2").Single();
        var estadosAntes = ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado || '/' || Decision AS Value FROM Carpetas ORDER BY Id").ToList();
        Assert.Equal((2, 2, 1), (porPedirAntes, solicitadoAntes, entrantesAntes));

        ctx.Database.Migrate();

        Assert.Contains("PendienteReclasificacion", Columnas(ctx, "Carpetas"));
        Assert.Equal(7, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE PendienteReclasificacion = 0").Single());
        Assert.Equal(0, ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Carpetas WHERE PendienteReclasificacion <> 0").Single());
        Assert.Equal(estadosAntes, ctx.Database.SqlQueryRaw<string>("SELECT Id || '/' || Estado || '/' || Decision AS Value FROM Carpetas ORDER BY Id").ToList());
        Assert.Equal(porPedirAntes, await new PedidosRepository(ctx).ContarPorPedirAsync());
        Assert.Equal(solicitadoAntes, await ctx.Carpetas.CountAsync(c => c.Estado == Sgl.Domain.EstadoCarpeta.CambioDomicilioSolicitado));
        Assert.Equal(entrantesAntes, await ctx.Carpetas.CountAsync(c => c.Estado == Sgl.Domain.EstadoCarpeta.CambioDomicilio && c.SentidoCambioDomicilio == Sgl.Domain.SentidoCambioDomicilio.Entrante && !c.PendienteReclasificacion));
        Assert.Equal(0, await new ReclasificacionRepository(ctx).ContarAsync());
    }

    private static List<string> Columnas(SglDbContext ctx, string tabla) =>
        ctx.Database.SqlQuery<string>($"SELECT name AS Value FROM pragma_table_info({tabla})").ToList();

    /// <summary>Inserts a synthetic row filling every NOT NULL column without default (the old schema cannot be reached through the entities).</summary>
    private static string InsertarFilaSintetica(SglDbContext ctx, string tabla, string? ciudadanoId, int? pendiente)
    {
        var id = Guid.NewGuid().ToString().ToUpperInvariant();
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        var columnas = new List<(string Nombre, string Tipo, bool NoNulo, bool ConDefecto)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT name, type, \"notnull\", dflt_value IS NOT NULL FROM pragma_table_info('{tabla}')";
            using var r = cmd.ExecuteReader();
            while (r.Read()) columnas.Add((r.GetString(0), r.GetString(1), r.GetInt32(2) == 1, r.GetInt32(3) == 1));
        }

        var nombres = new List<string>();
        var valores = new List<string>();
        foreach (var (nombre, tipo, noNulo, conDefecto) in columnas)
        {
            string? valor = nombre switch
            {
                "Id" => $"'{id}'",
                "CiudadanoId" => $"'{ciudadanoId}'",
                "PendienteBusqueda" when pendiente is not null => pendiente.ToString(),
                _ when noNulo && !conDefecto => tipo.StartsWith("INT", StringComparison.OrdinalIgnoreCase) ? "0" : $"'{(nombre == "Rut" ? Guid.NewGuid().ToString("N")[..10] : "x")}'",
                _ => null,
            };
            if (valor is null) continue;
            nombres.Add($"\"{nombre}\"");
            valores.Add(valor);
        }

        using var insert = conn.CreateCommand();
        insert.CommandText = $"INSERT INTO \"{tabla}\" ({string.Join(",", nombres)}) VALUES ({string.Join(",", valores)})";
        insert.ExecuteNonQuery();
        return id;
    }
}
