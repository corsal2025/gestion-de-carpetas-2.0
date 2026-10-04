using System.Globalization;

namespace Sgl.Domain;

/// <summary>A citizen's license folder. Every mutation is recorded in <see cref="Historial"/>.</summary>
public sealed class Carpeta
{
    public const int MaxUsuarioLength = 100;
    public const int MaxIdoneidadLength = 200;

    private readonly List<HistorialCambio> _historial = [];

    public Guid Id { get; private set; }

    public Guid CiudadanoId { get; private set; }

    public Ciudadano Ciudadano { get; private set; } = null!;

    public Sede Sede { get; private set; }

    public DateOnly FechaCitacion { get; private set; }

    public DateOnly? FechaSubida { get; private set; }

    public EstadoCarpeta Estado { get; private set; }

    public Decision Decision { get; private set; }

    public string? IdoneidadMoral { get; private set; }

    public string? CajaArchivo { get; private set; }

    public string? FechaUltimaCarpeta { get; private set; }

    public string? TipoTramite { get; private set; }

    public IReadOnlyList<HistorialCambio> Historial => _historial;

    private Carpeta()
    {
    }

    public static Carpeta CrearImportada(
        Guid id,
        Ciudadano ciudadano,
        Sede sede,
        DateOnly fechaCitacion,
        DateOnly? fechaSubida,
        string? fechaUltimaCarpeta,
        EstadoCarpeta estado,
        Decision decision,
        string? idoneidadMoral,
        string? tipoTramite,
        string? cajaArchivo,
        DateTime now)
    {
        var carpeta = new Carpeta
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            Ciudadano = ciudadano,
            CiudadanoId = ciudadano.Id,
            Sede = sede,
            FechaCitacion = fechaCitacion,
            FechaSubida = fechaSubida,
            FechaUltimaCarpeta = fechaUltimaCarpeta,
            Estado = estado,
            Decision = decision,
            IdoneidadMoral = idoneidadMoral,
            TipoTramite = tipoTramite,
            CajaArchivo = cajaArchivo,
        };
        carpeta.Audit(now, "Sistema Importador", "Importacion", null, $"{sede} - {tipoTramite ?? estado.ToString()}");
        return carpeta;
    }

    public void AsignarCaja(string? nuevaCaja, string autor, DateTime now)
    {
        var usuario = RequireAutor(autor);
        var caja = string.IsNullOrWhiteSpace(nuevaCaja) ? null : nuevaCaja.Trim();
        if (caja != CajaArchivo)
        {
            Audit(now, usuario, nameof(CajaArchivo), CajaArchivo, caja);
            CajaArchivo = caja;
        }
    }

    public static Carpeta Registrar(Ciudadano ciudadano, Sede sede, DateOnly fechaCitacion, string autor, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(ciudadano);
        var usuario = RequireAutor(autor);
        RequireSede(sede);

        var carpeta = new Carpeta
        {
            Id = Guid.NewGuid(),
            Ciudadano = ciudadano,
            CiudadanoId = ciudadano.Id,
            Sede = sede,
            FechaCitacion = fechaCitacion,
            Estado = EstadoCarpeta.Citada,
            Decision = Decision.Pendiente,
        };
        carpeta.Audit(now, usuario, "Registro", null, $"{EstadoCarpeta.Citada} ({sede})");
        return carpeta;
    }

    public void CambiarEstado(EstadoCarpeta nuevo, string autor, DateTime now)
    {
        var usuario = RequireAutor(autor);
        if (!EstadoTransitions.CanTransition(Estado, nuevo))
        {
            throw new InvalidTransitionException(Estado, nuevo);
        }

        Audit(now, usuario, nameof(Estado), Estado.ToString(), nuevo.ToString());
        Estado = nuevo;

        var decision = EstadoTransitions.DecisionFor(nuevo);
        if (decision != Decision)
        {
            Audit(now, usuario, nameof(Decision), Decision.ToString(), decision.ToString());
            Decision = decision;
        }
    }

    public void ForzarEstado(EstadoCarpeta nuevo, string autor, DateTime now)
    {
        var usuario = RequireAutor(autor);
        if (nuevo != Estado)
        {
            Audit(now, usuario, nameof(Estado), Estado.ToString(), nuevo.ToString());
            Estado = nuevo;

            var decision = EstadoTransitions.DecisionFor(nuevo);
            if (decision != Decision)
            {
                Audit(now, usuario, nameof(Decision), Decision.ToString(), decision.ToString());
                Decision = decision;
            }

            if (EsEstadoSubida(nuevo) && FechaSubida is null)
            {
                FechaSubida = DateOnly.FromDateTime(now);
                Audit(now, usuario, nameof(FechaSubida), null, FechaSubida.Value.ToString("yyyy-MM-dd"));
            }
        }
    }

    private static bool EsEstadoSubida(EstadoCarpeta estado) => estado switch
    {
        EstadoCarpeta.SubidaConaset or
        EstadoCarpeta.PrimeraLicencia or
        EstadoCarpeta.Otorgado => true,
        _ => false
    };

    public void CambiarDecision(Decision nueva, string autor, DateTime now)
    {
        var usuario = RequireAutor(autor);
        if (nueva != Decision)
        {
            Audit(now, usuario, nameof(Decision), Decision.ToString(), nueva.ToString());
            Decision = nueva;

            if (nueva is Decision.Otorgado or Decision.Denegado && FechaSubida is null)
            {
                FechaSubida = DateOnly.FromDateTime(now);
                Audit(now, usuario, nameof(FechaSubida), null, FechaSubida.Value.ToString("yyyy-MM-dd"));
            }
        }
    }

    public void Editar(Sede sede, DateOnly fechaCitacion, DateOnly? fechaSubida, string? idoneidadMoral, string autor, DateTime now)
    {
        var usuario = RequireAutor(autor);
        RequireSede(sede);
        if (fechaSubida is { } subida && subida < fechaCitacion)
        {
            throw new DomainException("La fecha de subida no puede ser anterior a la fecha de citación.");
        }

        var idoneidad = string.IsNullOrWhiteSpace(idoneidadMoral) ? null : idoneidadMoral.Trim();
        if (idoneidad?.Length > MaxIdoneidadLength)
        {
            throw new DomainException($"Idoneidad moral excede {MaxIdoneidadLength} caracteres.");
        }

        if (sede != Sede)
        {
            Audit(now, usuario, nameof(Sede), Sede.ToString(), sede.ToString());
            Sede = sede;
        }

        if (fechaCitacion != FechaCitacion)
        {
            Audit(now, usuario, nameof(FechaCitacion), Format(FechaCitacion), Format(fechaCitacion));
            FechaCitacion = fechaCitacion;
        }

        if (fechaSubida != FechaSubida)
        {
            Audit(now, usuario, nameof(FechaSubida), Format(FechaSubida), Format(fechaSubida));
            FechaSubida = fechaSubida;
        }

        if (idoneidad != IdoneidadMoral)
        {
            Audit(now, usuario, nameof(IdoneidadMoral), IdoneidadMoral, idoneidad);
            IdoneidadMoral = idoneidad;
        }
    }

    public void ModificarRut(Rut nuevoRut, Ciudadano? ciudadanoExistente, string autor, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(nuevoRut);
        var usuario = RequireAutor(autor);
        if (Ciudadano.Rut != nuevoRut)
        {
            var anterior = Ciudadano.Rut.Formatted;
            if (ciudadanoExistente is not null && ciudadanoExistente.Id != CiudadanoId)
            {
                Ciudadano = ciudadanoExistente;
                CiudadanoId = ciudadanoExistente.Id;
            }
            else
            {
                Ciudadano.CambiarRut(nuevoRut);
            }
            Audit(now, usuario, "RUT", anterior, nuevoRut.Formatted);
        }
    }

    private void Audit(DateTime now, string usuario, string campo, string? anterior, string? nuevo) =>
        _historial.Add(new HistorialCambio(Id, now, usuario, campo, anterior, nuevo));

    private static string? Format(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string RequireAutor(string autor)
    {
        if (string.IsNullOrWhiteSpace(autor))
        {
            throw new DomainException("El autor del cambio es obligatorio.");
        }

        var trimmed = autor.Trim();
        return trimmed.Length > MaxUsuarioLength
            ? throw new DomainException($"El autor excede {MaxUsuarioLength} caracteres.")
            : trimmed;
    }

    private static void RequireSede(Sede sede)
    {
        if (!Enum.IsDefined(sede))
        {
            throw new DomainException("Sede inválida.");
        }
    }
}
