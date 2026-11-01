namespace Sgl.Domain;

public sealed partial class Carpeta
{
    /// <summary>Registro explícito del respaldo opcional; las carpetas antiguas no se incorporan solas.</summary>
    public DateTime? FechaRespaldoSolicitado { get; private set; }
    public DateTime? FechaRespaldoSubido { get; private set; }
    public bool RespaldoPendiente => FechaRespaldoSolicitado is not null && FechaRespaldoSubido is null;

    public void AgregarRespaldoPenultima(string autor, DateTime now)
    {
        RequireActiva();
        var usuario = RequireAutor(autor);
        if (RespaldoPendiente) throw new DomainException("El respaldo ya está pendiente.");
        FechaRespaldoSolicitado = now;
        FechaRespaldoSubido = null;
        Audit(now, usuario, "RespaldoPenultima", null, "Pendiente");
    }

    public void ValidarRespaldoPendiente()
    {
        RequireActiva();
        if (!RespaldoPendiente) throw new DomainException("La carpeta no tiene un respaldo pendiente.");
    }

    public void MarcarRespaldoPenultimaSubido(string autor, DateTime now)
    {
        ValidarRespaldoPendiente();
        var usuario = RequireAutor(autor);
        FechaRespaldoSubido = now;
        Audit(now, usuario, "RespaldoPenultima", "Pendiente", "Subido");
    }

    public void CancelarRespaldoPenultima(string autor, DateTime now)
    {
        ValidarRespaldoPendiente();
        var usuario = RequireAutor(autor);
        FechaRespaldoSolicitado = null;
        FechaRespaldoSubido = null;
        Audit(now, usuario, "RespaldoPenultima", "Pendiente", "Cancelado");
    }
}
