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

    public IReadOnlyList<HistorialCambio> Historial => _historial;

    private Carpeta()
    {
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
