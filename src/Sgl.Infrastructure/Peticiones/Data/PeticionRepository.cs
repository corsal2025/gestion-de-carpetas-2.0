using Microsoft.Data.Sqlite;
using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Data;

/// <summary>SQLite sin ORM (mismo enfoque que LicenciasCarpetas). Un archivo data/peticiones.db.</summary>
public sealed class PeticionRepository : IDisposable
{
    private const int MaxBackups = 30;

    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly string _backupDirectory;

    public PeticionRepository(string dbPath, string? backupDirectory = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _dbPath = dbPath;
        _backupDirectory = backupDirectory ?? Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
        // Sin pool: cada operación abre y cierra su conexión, así el archivo queda libre al terminar
        // y no se acumulan conexiones vivas en un servicio de larga duración.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();
        Init();
    }

    /// <summary>
    /// Crea una copia consistente de SQLite antes de una operación destructiva.
    /// Conserva los últimos <see cref="MaxBackups"/> respaldos y permite guardarlos fuera
    /// del directorio de la aplicación.
    /// </summary>
    public string? Backup(string motivo)
    {
        try
        {
            if (!File.Exists(_dbPath))
            {
                return null;
            }

            Directory.CreateDirectory(_backupDirectory);

            var slug = new string(motivo.Where(char.IsLetterOrDigit).ToArray());
            var destino = Path.Combine(_backupDirectory, $"peticiones-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{slug}.db");
            using (var source = new SqliteConnection(_connectionString))
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destino }.ToString()))
            {
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }

            // Retención separada: los automáticos diarios no desplazan a los manuales (los que el
            // operador pidió antes de una operación destructiva) ni al revés.
            var todos = new DirectoryInfo(_backupDirectory).GetFiles("peticiones-*.db");
            var grupos = todos.GroupBy(f => f.Name.EndsWith("-automatico.db", StringComparison.OrdinalIgnoreCase));
            foreach (var viejo in grupos.SelectMany(g => g.OrderByDescending(f => f.Name).Skip(MaxBackups)))
            {
                try { viejo.Delete(); } catch { /* respaldo viejo, no crítico */ }
            }

            return destino;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return null;
        }
    }

    public string? BackupDiario()
    {
        var prefijo = $"peticiones-{DateTime.Now:yyyyMMdd}-";
        if (Directory.Exists(_backupDirectory))
        {
            var existente = new DirectoryInfo(_backupDirectory)
                .GetFiles($"{prefijo}*-automatico.db")
                .OrderByDescending(f => f.Name)
                .FirstOrDefault();
            if (existente is not null)
            {
                return existente.FullName;
            }
        }

        return Backup("automatico");
    }

    private void Init()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Peticion (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                NombreCompleto    TEXT NOT NULL,
                Rut               TEXT NOT NULL,
                Comuna            TEXT NOT NULL,
                Clases            TEXT,
                FechaSolicitud    TEXT,
                Origen            TEXT,
                RutInvalido       INTEGER NOT NULL DEFAULT 0,
                Estado            INTEGER NOT NULL DEFAULT 0,
                DetalleEstado     TEXT,
                CreadaEn          TEXT NOT NULL,
                EnviadaEn         TEXT,
                DestinatariosCorreo TEXT,
                Oficina           TEXT,
                OrdenImportacion  INTEGER NOT NULL DEFAULT 0,
                Marcada           INTEGER NOT NULL DEFAULT 0,
                EstadoCarpeta     TEXT NOT NULL DEFAULT 'CAMBIO DE DOMICILIO',
                SubidaEn          TEXT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_Peticion_Rut_Comuna
                ON Peticion (Rut, Comuna);
            """;
        cmd.ExecuteNonQuery();

        // Bases creadas antes de estas columnas: agregarlas sin perder datos.
        foreach (var (columna, definicion) in new[]
                 {
                     ("Oficina", "TEXT"),
                     ("OrdenImportacion", "INTEGER NOT NULL DEFAULT 0"),
                     ("Marcada", "INTEGER NOT NULL DEFAULT 0"),
                     ("EstadoCarpeta", "TEXT NOT NULL DEFAULT 'CAMBIO DE DOMICILIO'"),
                     ("SubidaEn", "TEXT"),
                 })
        {
            if (ColumnExists(cn, columna))
            {
                continue;
            }

            using var alter = cn.CreateCommand();
            alter.CommandText = $"ALTER TABLE Peticion ADD COLUMN {columna} {definicion};";
            alter.ExecuteNonQuery();
        }

        // Un "Enviando" al arrancar es un envío cortado a mitad (cierre del proceso): el correo pudo
        // haber salido o no. Queda en Error con aviso para que el operador revise "Enviados" antes de reintentar.
        using var huerfanos = cn.CreateCommand();
        huerfanos.CommandText = """
            UPDATE Peticion
               SET Estado = $error,
                   DetalleEstado = 'El envío se interrumpió. Revisá la carpeta Enviados del buzón antes de reintentar.'
             WHERE Estado = $enviando;
            """;
        huerfanos.Parameters.AddWithValue("$error", (int)EstadoPeticion.Error);
        huerfanos.Parameters.AddWithValue("$enviando", (int)EstadoPeticion.Enviando);
        huerfanos.ExecuteNonQuery();

        // Invariante: SubidaEn con valor ⟺ la carpeta figura subida. Reconcilia filas de
        // bases viejas donde se revirtió el estado sin limpiar la fecha.
        using var fix = cn.CreateCommand();
        fix.CommandText = """
            UPDATE Peticion SET SubidaEn = NULL
             WHERE SubidaEn IS NOT NULL
               AND EstadoCarpeta NOT IN (
                   'CAMBIO DOM. SUBIDO A CONASET', 'CAMBIO DOM. SUBIDO CON CORREO',
                   'SUBIDA A CONASET', 'SUBIDA CON F8', 'SUBIDA CON OFICIO');
            """;
        fix.ExecuteNonQuery();

        // Comunas que estuvieron mal escritas en el directorio: las peticiones guardadas con el
        // nombre viejo pasan al corregido. Si no, la próxima importación calza con el nombre nuevo,
        // no encuentra la clave (Rut, Comuna) y crea un duplicado que se volvería a enviar.
        // Se salta la fila si ya existe la misma persona con el nombre corregido (índice único).
        foreach (var (mal, bien) in new[] { ("CONCECPION", "CONCEPCION") })
        {
            using var rename = cn.CreateCommand();
            rename.CommandText = """
                UPDATE Peticion SET Comuna = $bien
                 WHERE Comuna = $mal
                   AND NOT EXISTS (SELECT 1 FROM Peticion p2 WHERE p2.Rut = Peticion.Rut AND p2.Comuna = $bien);
                """;
            rename.Parameters.AddWithValue("$mal", mal);
            rename.Parameters.AddWithValue("$bien", bien);
            rename.ExecuteNonQuery();
        }
    }

    private static bool ColumnExists(SqliteConnection cn, string column)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Peticion') WHERE name = $name;";
        cmd.Parameters.AddWithValue("$name", column);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private SqliteConnection Open()
    {
        var cn = new SqliteConnection(_connectionString);
        cn.Open();
        // Base compartida con EF y con el servicio de respaldo: WAL + espera ante bloqueos.
        using var pragma = cn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return cn;
    }

    /// <summary>No retiene conexiones (cada operación cierra la suya); existe por compatibilidad con <c>using</c>.</summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Inserta la peticion; si ya existe una con el mismo (Rut, Comuna), REFRESCA los campos que
    /// vienen del Excel — nombre, clases, fecha, origen, oficina, orden y validez del RUT — porque
    /// el libro es la fuente de la verdad para esos datos.
    /// NUNCA pisa lo que puso el operador: Marcada, Estado, DetalleEstado, EnviadaEn,
    /// DestinatariosCorreo y CreadaEn quedan como estaban.
    /// Devuelve true si era nueva, false si ya existia y se refresco.
    /// </summary>
    public bool AddIfNew(Peticion p)
    {
        using var cn = Open();

        bool yaExistia;
        using (var check = cn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM Peticion WHERE Rut = $rut AND Comuna = $comuna;";
            check.Parameters.AddWithValue("$rut", p.Rut);
            check.Parameters.AddWithValue("$comuna", p.Comuna);
            yaExistia = Convert.ToInt64(check.ExecuteScalar()) > 0;
        }

        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Peticion
                (NombreCompleto, Rut, Comuna, Clases, FechaSolicitud, Origen, Oficina,
                 OrdenImportacion, RutInvalido, Estado, DetalleEstado, CreadaEn,
                 EnviadaEn, DestinatariosCorreo, Marcada, EstadoCarpeta)
            VALUES
                ($nombre, $rut, $comuna, $clases, $fecha, $origen, $oficina,
                 $orden, $rutInvalido, $estado, $detalle, $creada,
                 $enviadaEn, $destinatarios, $marcada, $estadoCarpeta)
            ON CONFLICT (Rut, Comuna) DO UPDATE SET
                NombreCompleto   = excluded.NombreCompleto,
                Clases           = excluded.Clases,
                FechaSolicitud   = excluded.FechaSolicitud,
                Origen           = excluded.Origen,
                Oficina          = excluded.Oficina,
                OrdenImportacion = excluded.OrdenImportacion,
                RutInvalido      = excluded.RutInvalido;
            """;
        cmd.Parameters.AddWithValue("$nombre", p.NombreCompleto);
        cmd.Parameters.AddWithValue("$rut", p.Rut);
        cmd.Parameters.AddWithValue("$comuna", p.Comuna);
        cmd.Parameters.AddWithValue("$clases", (object?)p.Clases ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fecha", (object?)p.FechaSolicitud?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$origen", (object?)p.Origen ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$oficina", (object?)p.Oficina ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$orden", p.OrdenImportacion);
        cmd.Parameters.AddWithValue("$rutInvalido", p.RutInvalido ? 1 : 0);
        cmd.Parameters.AddWithValue("$estado", (int)p.Estado);
        cmd.Parameters.AddWithValue("$detalle", (object?)p.DetalleEstado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$creada", p.CreadaEn.ToString("o"));
        cmd.Parameters.AddWithValue("$enviadaEn", (object?)p.EnviadaEn?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$destinatarios", (object?)p.DestinatariosCorreo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$marcada", p.Marcada ? 1 : 0);
        cmd.Parameters.AddWithValue("$estadoCarpeta", p.EstadoCarpeta);
        cmd.ExecuteNonQuery();

        return !yaExistia;
    }

    public IReadOnlyList<Peticion> All()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        // Tres bloques:
        //   0 · pendientes  → arriba, en el orden del Excel
        //   1 · enviadas sin resolver → al medio, de la más nueva a la más antigua
        //   2 · resueltas (carpeta subida) → al final, por orden de resolución (SubidaEn ascendente)
        // SubidaEn / EnviadaEn son ISO 8601, el orden de texto coincide con el cronológico.
        cmd.CommandText = """
            SELECT * FROM Peticion
             ORDER BY CASE WHEN SubidaEn IS NOT NULL THEN 2
                           WHEN Estado = 1 THEN 1
                           ELSE 0 END ASC,
                      CASE WHEN SubidaEn IS NOT NULL THEN SubidaEn END ASC,
                      EnviadaEn DESC,
                      OrdenImportacion ASC,
                      Id ASC;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<Peticion>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public Peticion? Get(long id)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Peticion WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Peticion? GetByRutComuna(string rut, string comuna)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Peticion WHERE Rut = $rut AND Comuna = $comuna COLLATE NOCASE LIMIT 1;";
        cmd.Parameters.AddWithValue("$rut", rut);
        cmd.Parameters.AddWithValue("$comuna", comuna);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// Guarda o actualiza una petición ingresada manualmente.
    /// Si ya existía, reactiva el estado a Borrador y limpia el envío anterior para permitir que se tramite nuevamente.
    /// Devuelve el Id de la petición.
    /// </summary>
    public long UpsertManual(Peticion p)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Peticion
                (NombreCompleto, Rut, Comuna, Clases, FechaSolicitud, Origen, Oficina,
                 OrdenImportacion, RutInvalido, Estado, DetalleEstado, CreadaEn,
                 EnviadaEn, DestinatariosCorreo, Marcada, EstadoCarpeta)
            VALUES
                ($nombre, $rut, $comuna, $clases, $fecha, $origen, $oficina,
                 $orden, $rutInvalido, $estado, $detalle, $creada,
                 NULL, NULL, 0, $estadoCarpeta)
            ON CONFLICT (Rut, Comuna) DO UPDATE SET
                NombreCompleto   = excluded.NombreCompleto,
                Origen           = excluded.Origen,
                Oficina          = excluded.Oficina,
                OrdenImportacion = excluded.OrdenImportacion,
                RutInvalido      = excluded.RutInvalido,
                EstadoCarpeta    = excluded.EstadoCarpeta,
                Estado           = CASE WHEN Estado = $enviando THEN Estado ELSE excluded.Estado END,
                SubidaEn         = CASE WHEN Estado = $enviando THEN SubidaEn ELSE NULL END,
                DetalleEstado    = CASE WHEN Estado = $enviando THEN DetalleEstado ELSE NULL END,
                EnviadaEn        = CASE WHEN Estado = $enviando THEN EnviadaEn ELSE NULL END,
                DestinatariosCorreo = CASE WHEN Estado = $enviando THEN DestinatariosCorreo ELSE NULL END,
                Marcada          = CASE WHEN Estado = $enviando THEN Marcada ELSE 0 END;
            SELECT Id FROM Peticion WHERE Rut = $rut AND Comuna = $comuna;
            """;
        cmd.Parameters.AddWithValue("$nombre", p.NombreCompleto);
        cmd.Parameters.AddWithValue("$rut", p.Rut);
        cmd.Parameters.AddWithValue("$comuna", p.Comuna);
        cmd.Parameters.AddWithValue("$clases", (object?)p.Clases ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fecha", (object?)p.FechaSolicitud?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$origen", (object?)p.Origen ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$oficina", (object?)p.Oficina ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$orden", p.OrdenImportacion);
        cmd.Parameters.AddWithValue("$rutInvalido", p.RutInvalido ? 1 : 0);
        cmd.Parameters.AddWithValue("$estado", (int)EstadoPeticion.Borrador);
        cmd.Parameters.AddWithValue("$enviando", (int)EstadoPeticion.Enviando);
        cmd.Parameters.AddWithValue("$detalle", DBNull.Value);
        cmd.Parameters.AddWithValue("$creada", p.CreadaEn.ToString("o"));
        cmd.Parameters.AddWithValue("$estadoCarpeta", p.EstadoCarpeta);

        var scalar = cmd.ExecuteScalar();
        return Convert.ToInt64(scalar);
    }

    /// <summary>
    /// Reserva atómica para enviar: pasa la petición de pendiente (Borrador / Sin correo / Error) a
    /// "Enviando". Devuelve false si otro envío ya la tomó o ya está enviada: quien recibe false no manda nada.
    /// </summary>
    public bool TryMarcarEnviando(long id)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE Peticion
               SET Estado = $enviando, DetalleEstado = 'Enviando…'
             WHERE Id = $id
               AND Estado IN ($borrador, $sinCorreo, $error);
            """;
        cmd.Parameters.AddWithValue("$enviando", (int)EstadoPeticion.Enviando);
        cmd.Parameters.AddWithValue("$borrador", (int)EstadoPeticion.Borrador);
        cmd.Parameters.AddWithValue("$sinCorreo", (int)EstadoPeticion.SinCorreoComuna);
        cmd.Parameters.AddWithValue("$error", (int)EstadoPeticion.Error);
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    public void UpdateEstado(long id, EstadoPeticion estado, string? detalle, DateTimeOffset? enviadaEn, string? destinatarios)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE Peticion
               SET Estado = $estado,
                   DetalleEstado = $detalle,
                   EnviadaEn = $enviada,
                   DestinatariosCorreo = $dest,
                   Marcada = CASE WHEN $limpiarMarca = 1 THEN 0 ELSE Marcada END
             WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$estado", (int)estado);
        cmd.Parameters.AddWithValue("$limpiarMarca", estado == EstadoPeticion.Enviada ? 1 : 0);
        cmd.Parameters.AddWithValue("$detalle", (object?)detalle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enviada", (object?)enviadaEn?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dest", (object?)destinatarios ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Marca las filas seleccionadas como enviadas sin disparar correo alguno hacia la comuna.
    /// Sirve para cerrar el flujo local cuando la carpeta ya quedó resuelta o se quiere dejar como
    /// "ya enviada" sin que el sistema mande mails a otras municipalidades.
    /// </summary>
    public int MarcarMarcadasComoEnviadasSinCorreo(DateTimeOffset? enviadaEn = null)
    {
        var cuando = enviadaEn ?? DateTimeOffset.Now;
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE Peticion
               SET Estado = $estadoEnviada,
                   DetalleEstado = $detalle,
                   EnviadaEn = $enviada,
                   DestinatariosCorreo = NULL,
                   Marcada = 0,
                   EstadoCarpeta = CASE
                       WHEN EstadoCarpeta = $carpetaOriginal THEN $carpetaSolicitada
                       ELSE EstadoCarpeta
                   END
             WHERE Marcada = 1
               AND Estado IN ($estadoBorrador, $estadoSinCorreo, $estadoError);
            SELECT changes();
            """;
        cmd.Parameters.AddWithValue("$estadoEnviada", (int)EstadoPeticion.Enviada);
        cmd.Parameters.AddWithValue("$detalle", "Marcada como enviada sin enviar correo a la comuna.");
        cmd.Parameters.AddWithValue("$enviada", cuando.ToString("o"));
        cmd.Parameters.AddWithValue("$carpetaOriginal", EstadoCarpetaCatalog.CambioDeDomicilio);
        cmd.Parameters.AddWithValue("$carpetaSolicitada", EstadoCarpetaCatalog.SinSubir);
        cmd.Parameters.AddWithValue("$estadoBorrador", (int)EstadoPeticion.Borrador);
        cmd.Parameters.AddWithValue("$estadoSinCorreo", (int)EstadoPeticion.SinCorreoComuna);
        cmd.Parameters.AddWithValue("$estadoError", (int)EstadoPeticion.Error);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Marca todas las peticiones pendientes como ya enviadas localmente sin disparar correo externo.
    /// Útil para dejar el tablero como "todo subido" cuando se quiere cerrar el flujo sin tocar otras comunas.
    /// </summary>
    public int MarcarTodasComoEnviadasSinCorreo(DateTimeOffset? enviadaEn = null)
    {
        var cuando = enviadaEn ?? DateTimeOffset.Now;
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE Peticion
               SET Estado = $estadoEnviada,
                   DetalleEstado = $detalle,
                   EnviadaEn = $enviada,
                   DestinatariosCorreo = NULL,
                   Marcada = 0,
                   EstadoCarpeta = CASE
                       WHEN EstadoCarpeta = $carpetaOriginal THEN $carpetaSolicitada
                       ELSE EstadoCarpeta
                   END
             WHERE Estado IN ($estadoBorrador, $estadoSinCorreo, $estadoError);
            SELECT changes();
            """;
        cmd.Parameters.AddWithValue("$estadoEnviada", (int)EstadoPeticion.Enviada);
        cmd.Parameters.AddWithValue("$detalle", "Marcada como enviada sin enviar correo a la comuna.");
        cmd.Parameters.AddWithValue("$enviada", cuando.ToString("o"));
        cmd.Parameters.AddWithValue("$carpetaOriginal", EstadoCarpetaCatalog.CambioDeDomicilio);
        cmd.Parameters.AddWithValue("$carpetaSolicitada", EstadoCarpetaCatalog.SinSubir);
        cmd.Parameters.AddWithValue("$estadoBorrador", (int)EstadoPeticion.Borrador);
        cmd.Parameters.AddWithValue("$estadoSinCorreo", (int)EstadoPeticion.SinCorreoComuna);
        cmd.Parameters.AddWithValue("$estadoError", (int)EstadoPeticion.Error);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Cambia el estado de la carpeta (desplegable estilo Excel). Si el nuevo estado es de "subida"
    /// y todavía no hay <c>SubidaEn</c>, la estampa con la fecha de hoy — así el cierre manual
    /// también cuenta para la estadística de demora.
    /// </summary>
    public void SetEstadoCarpeta(long id, string estado)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        if (Domain.EstadoCarpetaCatalog.Rango(estado) == 3)
        {
            cmd.CommandText = "UPDATE Peticion SET EstadoCarpeta = $e, SubidaEn = COALESCE(SubidaEn, $hoy) WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$hoy", DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"));
        }
        else
        {
            // Volver a un estado anterior (p. ej. "sin subir") descarta la fecha de resolución.
            cmd.CommandText = "UPDATE Peticion SET EstadoCarpeta = $e, SubidaEn = NULL WHERE Id = $id;";
        }

        cmd.Parameters.AddWithValue("$e", estado);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Sincroniza el estado de la carpeta desde el Excel. Solo AVANZA: si el Excel trae un estado
    /// de una etapa anterior o lateral, no toca nada. Cuando la carpeta pasa a "subida":
    ///  - guarda <paramref name="subidaEn"/> si aún no había fecha;
    ///  - si la petición seguía pendiente, la marca como Enviada (la comuna resolvió sin que
    ///    llegáramos a mandar el correo).
    /// Devuelve true si cambió algo.
    /// </summary>
    public bool SincronizarCarpetaDesdeExcel(long id, string estadoExcel, DateOnly? subidaEn)
    {
        var rangoNuevo = Domain.EstadoCarpetaCatalog.Rango(estadoExcel);
        if (rangoNuevo < 2)
        {
            return false;
        }

        using var cn = Open();

        string? estadoActual;
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT EstadoCarpeta FROM Peticion WHERE Id = $id;";
            q.Parameters.AddWithValue("$id", id);
            estadoActual = q.ExecuteScalar() as string;
        }

        if (estadoActual is null || Domain.EstadoCarpetaCatalog.Rango(estadoActual) >= rangoNuevo)
        {
            return false;
        }

        var canonico = Domain.EstadoCarpetaCatalog.Normalizar(estadoExcel);
        var fecha = subidaEn?.ToString("yyyy-MM-dd");

        using var cmd = cn.CreateCommand();
        if (rangoNuevo == 3)
        {
            cmd.CommandText = """
                UPDATE Peticion
                   SET EstadoCarpeta  = $e,
                       SubidaEn       = COALESCE(SubidaEn, $f),
                       Estado         = CASE WHEN Estado <> 1 THEN 1 ELSE Estado END,
                       EnviadaEn      = COALESCE(EnviadaEn, $f),
                       DetalleEstado  = CASE WHEN Estado <> 1
                            THEN 'Carpeta subida detectada en el Excel — la comuna resolvió sin nuestra solicitud'
                            ELSE DetalleEstado END
                 WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$f", (object?)fecha ?? DBNull.Value);
        }
        else
        {
            cmd.CommandText = "UPDATE Peticion SET EstadoCarpeta = $e WHERE Id = $id;";
        }

        cmd.Parameters.AddWithValue("$e", canonico);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return true;
    }

    /// <summary>Invierte la marca personal de una fila. Devuelve el valor que quedo.</summary>
    public bool ToggleMarcada(long id)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE Peticion SET Marcada = CASE Marcada WHEN 1 THEN 0 ELSE 1 END WHERE Id = $id;
            SELECT Marcada FROM Peticion WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        var result = cmd.ExecuteScalar();
        return result is not null && Convert.ToInt64(result) == 1;
    }

    /// <summary>Quita la marca de las filas indicadas. Devuelve cuantas se desmarcaron.</summary>
    public int DesmarcarIds(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        using var cn = Open();
        using var tx = cn.BeginTransaction();
        var total = 0;
        foreach (var id in ids)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Peticion SET Marcada = 0 WHERE Id = $id AND Marcada = 1;";
            cmd.Parameters.AddWithValue("$id", id);
            total += cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return total;
    }

    /// <summary>Quita la marca de TODAS las filas marcadas. Devuelve cuantas se desmarcaron.</summary>
    public int DesmarcarTodas()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE Peticion SET Marcada = 0 WHERE Marcada = 1;";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Borra las filas marcadas. Devuelve cuantas se borraron.</summary>
    public int DeleteMarcadas()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM Peticion WHERE Marcada = 1;";
        return cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM Peticion WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Borra todas las filas. Devuelve cuantas habia. El Excel no se toca.</summary>
    public int DeleteAll()
    {
        using var cn = Open();
        using var count = cn.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Peticion;";
        var n = Convert.ToInt32(count.ExecuteScalar());

        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM Peticion; DELETE FROM sqlite_sequence WHERE name = 'Peticion';";
        cmd.ExecuteNonQuery();
        return n;
    }

    private static Peticion Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        NombreCompleto = r.GetString(r.GetOrdinal("NombreCompleto")),
        Rut = r.GetString(r.GetOrdinal("Rut")),
        Comuna = r.GetString(r.GetOrdinal("Comuna")),
        Clases = r.IsDBNull(r.GetOrdinal("Clases")) ? null : r.GetString(r.GetOrdinal("Clases")),
        FechaSolicitud = r.IsDBNull(r.GetOrdinal("FechaSolicitud"))
            ? null
            : DateOnly.Parse(r.GetString(r.GetOrdinal("FechaSolicitud"))),
        Origen = r.IsDBNull(r.GetOrdinal("Origen")) ? null : r.GetString(r.GetOrdinal("Origen")),
        Oficina = r.IsDBNull(r.GetOrdinal("Oficina")) ? null : r.GetString(r.GetOrdinal("Oficina")),
        OrdenImportacion = r.GetInt64(r.GetOrdinal("OrdenImportacion")),
        Marcada = r.GetInt32(r.GetOrdinal("Marcada")) == 1,
        EstadoCarpeta = r.IsDBNull(r.GetOrdinal("EstadoCarpeta")) ? "CAMBIO DE DOMICILIO" : r.GetString(r.GetOrdinal("EstadoCarpeta")),
        RutInvalido = r.GetInt32(r.GetOrdinal("RutInvalido")) == 1,
        Estado = (EstadoPeticion)r.GetInt32(r.GetOrdinal("Estado")),
        DetalleEstado = r.IsDBNull(r.GetOrdinal("DetalleEstado")) ? null : r.GetString(r.GetOrdinal("DetalleEstado")),
        CreadaEn = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreadaEn"))),
        EnviadaEn = r.IsDBNull(r.GetOrdinal("EnviadaEn"))
            ? null
            : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("EnviadaEn"))),
        DestinatariosCorreo = r.IsDBNull(r.GetOrdinal("DestinatariosCorreo"))
            ? null
            : r.GetString(r.GetOrdinal("DestinatariosCorreo")),
        SubidaEn = r.IsDBNull(r.GetOrdinal("SubidaEn"))
            ? null
            : DateOnly.Parse(r.GetString(r.GetOrdinal("SubidaEn"))),
    };
}
