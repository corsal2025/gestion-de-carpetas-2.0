using Microsoft.EntityFrameworkCore;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

public sealed class SglDbContext(DbContextOptions<SglDbContext> options) : DbContext(options)
{
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try { return base.SaveChanges(acceptAllChangesOnSuccess); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrenciaException(); }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try { return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrenciaException(); }
    }

    public DbSet<Carpeta> Carpetas => Set<Carpeta>();

    public DbSet<Ciudadano> Ciudadanos => Set<Ciudadano>();

    public DbSet<HistorialCambio> Historial => Set<HistorialCambio>();

    public DbSet<RevisionDuplicado> RevisionesDuplicado => Set<RevisionDuplicado>();

    public DbSet<ImportacionLog> ImportacionLogs => Set<ImportacionLog>();

    public DbSet<IntegridadCorrida> IntegridadCorridas => Set<IntegridadCorrida>();

    public DbSet<SolicitudF8> SolicitudesF8 => Set<SolicitudF8>();

    public DbSet<SolicitudCertificado> SolicitudesCertificado => Set<SolicitudCertificado>();

    public DbSet<ConfiguracionItem> Configuracion => Set<ConfiguracionItem>();

    public DbSet<CitaCancelada> CitasCanceladas => Set<CitaCancelada>();

    public DbSet<Asistencia> Asistencias => Set<Asistencia>();

    public DbSet<CorreoCambioDomicilio> CorreosCambioDomicilio => Set<CorreoCambioDomicilio>();

    public DbSet<CorreoDescartadoRegistro> CorreosDescartados => Set<CorreoDescartadoRegistro>();

    public DbSet<EnvioCorreoComuna> EnviosCorreoComuna => Set<EnvioCorreoComuna>();

    public DbSet<EnvioCorreoComunaCarpeta> EnviosCorreoComunaCarpetas => Set<EnvioCorreoComunaCarpeta>();

    public DbSet<PlantillaCorreo> PlantillasCorreo => Set<PlantillaCorreo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ciudadano>(b =>
        {
            b.ToTable("Ciudadanos");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever().UseCollation("NOCASE");
            b.OwnsOne(c => c.Rut, r =>
            {
                r.Property(x => x.Value).HasColumnName("Rut").HasMaxLength(12).IsRequired();
                r.HasIndex(x => x.Value).IsUnique();
            });
            b.Navigation(c => c.Rut).IsRequired();
            b.Property(c => c.Nombre).HasMaxLength(Ciudadano.MaxNameLength).IsRequired();
            b.Property(c => c.Apellido).HasMaxLength(Ciudadano.MaxNameLength).IsRequired();
            b.Property(c => c.NombreBusqueda).HasMaxLength((Ciudadano.MaxNameLength * 2) + 1).IsRequired();
            b.HasIndex(c => c.NombreBusqueda);
        });

        modelBuilder.Entity<Carpeta>(b =>
        {
            b.ToTable("Carpetas");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever().UseCollation("NOCASE");
            b.Property(c => c.CiudadanoId).UseCollation("NOCASE");
            b.HasOne(c => c.Ciudadano).WithMany().HasForeignKey(c => c.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
            b.Property(c => c.Sede).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Estado).HasConversion<string>().HasMaxLength(50).IsConcurrencyToken();
            b.Property(c => c.Decision).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            b.Property(c => c.IdoneidadMoral).HasMaxLength(Carpeta.MaxIdoneidadLength);
            b.Property(c => c.CajaArchivo).HasMaxLength(50);
            b.Property(c => c.FechaUltimaCarpeta).HasMaxLength(50);
            b.Property(c => c.Comuna).HasMaxLength(Carpeta.MaxComunaLength);
            b.Property(c => c.TipoTramite).HasMaxLength(100);
            b.Property(c => c.PendienteBusqueda).HasDefaultValue(false);
            b.Property(c => c.CarpetaEncontrada).HasDefaultValue(false);
            b.Property(c => c.PendienteReclasificacion).HasDefaultValue(false);
            b.Property(c => c.Clases).HasColumnName("ClasesLicencia").HasDefaultValue(ClaseLicencia.Ninguna);
            b.Property(c => c.FechaRegistro);
            b.Property(c => c.TramiteAgenda).HasConversion<string>().HasMaxLength(30);
            b.Property(c => c.HoraCita);
            b.Property(c => c.ClasesAgenda);
            b.Property(c => c.EsExtra).HasDefaultValue(false);
            b.Property(c => c.EmailContacto).HasMaxLength(Carpeta.MaxEmailContactoLength);
            b.Property(c => c.TelefonoContacto).HasMaxLength(Carpeta.MaxTelefonoContactoLength);
            b.Property(c => c.Anulada).HasDefaultValue(false).IsConcurrencyToken();
            b.Property(c => c.MotivoAnulacion).HasMaxLength(MotivosAnulacion.MaxLength);
            b.Property(c => c.AnuladaPor).HasMaxLength(Carpeta.MaxUsuarioLength);
            b.Property(c => c.AnuladaEn);
            b.Property(c => c.FechaEstadoActual).IsConcurrencyToken();
            b.Property(c => c.FechaCierre);
            b.Property(c => c.FechaImpresion);
            b.Property(c => c.FechaPeticion);
            b.Property(c => c.CodigoF8).HasMaxLength(Carpeta.MaxCodigoF8Length);
            b.Property(c => c.FechaPenultimaCarpeta);
            b.Property(c => c.FechaRespaldoSolicitado).IsConcurrencyToken();
            b.Property(c => c.FechaRespaldoSubido).IsConcurrencyToken();
            b.Property(c => c.Resolucion);
            b.Property(c => c.FechaOtorgamiento);
            b.Property(c => c.SentidoCambioDomicilio);
            b.Property(c => c.RevisadaEn);
            b.Property(c => c.RutImportacion).HasMaxLength(12);
            // Soft delete: cancelled carpetas vanish from every query (modules, counts, statistics, duplicates).
            // Only the "Anuladas" view, restore and import idempotency opt out with IgnoreQueryFilters().
            b.HasQueryFilter(c => !c.Anulada);
            b.HasIndex(c => c.Anulada);
            b.HasIndex(c => new { c.CiudadanoId, c.Decision });
            b.HasMany(c => c.Historial).WithOne().HasForeignKey(h => h.CarpetaId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.Historial).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(c => c.Sede);
            b.HasIndex(c => c.Estado);
            b.HasIndex(c => c.CajaArchivo);
            b.HasIndex(c => c.TipoTramite);
            b.HasIndex(c => c.Comuna);
            b.HasIndex(c => new { c.FechaCitacion, c.Id });
            b.HasIndex(c => c.FechaSubida);
            b.HasIndex(c => new { c.CiudadanoId, c.FechaTomaCita });
            b.HasIndex(c => new { c.Decision, c.Estado });
            b.HasIndex(c => new { c.SentidoCambioDomicilio, c.Estado });
            b.HasIndex(c => c.RutImportacion);
        });

        modelBuilder.Entity<CitaCancelada>(b =>
        {
            b.ToTable("CitasCanceladas");
            b.HasKey(x => x.Id);
            b.Property(x => x.Sede).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Tramite).HasMaxLength(CitaCancelada.MaxTramiteLength).IsRequired();
            b.Property(x => x.Motivo).HasMaxLength(CitaCancelada.MaxMotivoLength);
            b.Property(x => x.IdentidadAgenda).HasMaxLength(64);
            b.HasIndex(x => x.IdentidadAgenda).IsUnique();
            b.HasIndex(x => new { x.FechaTomaCita, x.FechaCita, x.HoraCita, x.Sede, x.Tramite });
            b.HasIndex(x => x.FechaCita);
        });

        modelBuilder.Entity<Asistencia>(b =>
        {
            b.ToTable("Asistencias");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever().UseCollation("NOCASE");
            b.Property(a => a.CarpetaId).UseCollation("NOCASE");
            b.HasOne<Carpeta>().WithMany().HasForeignKey(a => a.CarpetaId).OnDelete(DeleteBehavior.Restrict);
            b.Property(a => a.Tipo).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.Rut).HasMaxLength(12).IsRequired();
            b.Property(a => a.Sede).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.Autor).HasMaxLength(Carpeta.MaxUsuarioLength).IsRequired();
            b.Property(a => a.AnuladaPor).HasMaxLength(Carpeta.MaxUsuarioLength);
            b.HasIndex(a => new { a.Fecha, a.Tipo });
            b.HasIndex(a => a.CarpetaId);
            // One mark (Vino or Extra) per person and day, enforced by the database too (the service checks first).
            b.HasIndex(a => new { a.Rut, a.Fecha }).IsUnique().HasFilter("\"Anulada\" = 0 AND \"Tipo\" <> 'PorAgregar'");
        });

        modelBuilder.Entity<HistorialCambio>(b =>
        {
            b.ToTable("HistorialCambios");
            b.HasKey(h => h.Id);
            b.Property(h => h.CarpetaId).UseCollation("NOCASE");
            b.Property(h => h.Usuario).HasMaxLength(Carpeta.MaxUsuarioLength).IsRequired();
            b.Property(h => h.Campo).HasMaxLength(50).IsRequired();
            b.Property(h => h.Anterior).HasMaxLength(250);
            b.Property(h => h.Nuevo).HasMaxLength(250);
            b.HasIndex(h => h.Fecha);
            b.HasIndex(h => new { h.Campo, h.CarpetaId, h.Fecha });
        });

        modelBuilder.Entity<ImportacionLog>(b =>
        {
            b.ToTable("ImportacionLogs");
            b.HasKey(i => i.Id);
            b.Property(i => i.Usuario).HasMaxLength(ImportacionLog.MaxUsuarioLength).IsRequired();
            b.HasIndex(i => i.Fecha);
        });

        modelBuilder.Entity<IntegridadCorrida>(b =>
        {
            b.ToTable("IntegridadCorridas");
            b.HasKey(i => i.Id);
            b.Property(i => i.Origen).HasMaxLength(IntegridadCorrida.MaxOrigenLength).IsRequired();
            b.Property(i => i.Detalle).HasMaxLength(IntegridadCorrida.MaxDetalleLength).IsRequired();
            b.HasIndex(i => i.Fecha);
        });

        modelBuilder.Entity<RevisionDuplicado>(b =>
        {
            b.ToTable("RevisionesDuplicado");
            b.HasKey(r => r.CiudadanoId);
            b.Property(r => r.CiudadanoId).ValueGeneratedNever().UseCollation("NOCASE");
            b.Property(r => r.RevisadoPor).HasMaxLength(Carpeta.MaxUsuarioLength).IsRequired();
            b.HasOne<Ciudadano>().WithMany().HasForeignKey(r => r.CiudadanoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SolicitudF8>(b =>
        {
            b.ToTable("SolicitudesF8");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever().UseCollation("NOCASE");
            b.Property(s => s.CarpetaId).UseCollation("NOCASE");
            b.HasOne<Carpeta>().WithMany().HasForeignKey(s => s.CarpetaId).OnDelete(DeleteBehavior.Restrict);
            b.Property(s => s.EstadoOrigen).HasConversion<string>().HasMaxLength(50);
            b.Property(s => s.SolicitadoPor).HasMaxLength(Carpeta.MaxUsuarioLength).IsRequired();
            b.Property(s => s.Estado).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.ResueltoPor).HasMaxLength(Carpeta.MaxUsuarioLength);
            b.Property(s => s.Observacion).HasMaxLength(SolicitudF8.MaxObservacionLength);
            b.HasIndex(s => new { s.Estado, s.SolicitadoEn });
            // At most one open request per carpeta, enforced by the database too (the service checks first).
            b.HasIndex(s => s.CarpetaId).IsUnique().HasFilter("[Estado] = 'Solicitado'");
        });

        modelBuilder.Entity<SolicitudCertificado>(b =>
        {
            b.ToTable("SolicitudesCertificado");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever().UseCollation("NOCASE");
            b.Property(s => s.CarpetaId).UseCollation("NOCASE");
            b.Property(s => s.SolicitudF8Id).UseCollation("NOCASE");
            b.HasOne<Carpeta>().WithMany().HasForeignKey(s => s.CarpetaId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SolicitudF8>().WithMany().HasForeignKey(s => s.SolicitudF8Id).OnDelete(DeleteBehavior.Restrict);
            b.Property(s => s.Tipo).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.Origen).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.Estado).HasConversion<string>().HasMaxLength(20);
            b.Property(s => s.SolicitadoPor).HasMaxLength(Carpeta.MaxUsuarioLength).IsRequired();
            b.Property(s => s.FirmadoPor).HasMaxLength(Carpeta.MaxUsuarioLength);
            b.Property(s => s.NumeroOficio).HasMaxLength(SolicitudCertificado.MaxNumeroOficioLength);
            b.Property(s => s.ParametrosJson).HasMaxLength(4000).IsRequired();
            b.Property(s => s.Observacion).HasMaxLength(SolicitudCertificado.MaxObservacionLength);
            b.HasIndex(s => new { s.Estado, s.SolicitadoEn });
            // At most one open certificate per carpeta and tipo, enforced by the database too.
            b.HasIndex(s => new { s.CarpetaId, s.Tipo }).IsUnique().HasFilter("[Estado] = 'PorFirmar'");
        });

        modelBuilder.Entity<ConfiguracionItem>(b =>
        {
            b.ToTable("Configuracion");
            b.HasKey(i => i.Clave);
            b.Property(i => i.Clave).HasMaxLength(ConfiguracionItem.MaxClaveLength);
            b.Property(i => i.Valor).HasMaxLength(ConfiguracionItem.MaxValorLength).IsRequired();
        });

        modelBuilder.Entity<CorreoCambioDomicilio>(b =>
        {
            b.ToTable("CorreosCambioDomicilio");
            b.HasKey(c => c.Id);
            b.Property(c => c.MessageId).HasMaxLength(CorreoCambioDomicilio.MaxMessageIdLength).IsRequired();
            b.Property(c => c.Rut).HasMaxLength(12).IsRequired();
            b.Property(c => c.CarpetaId).UseCollation("NOCASE");
            b.HasOne<Carpeta>().WithMany().HasForeignKey(c => c.CarpetaId).OnDelete(DeleteBehavior.Cascade);
            // Idempotency: one person per message, enforced by the database too.
            b.HasIndex(c => new { c.MessageId, c.Rut }).IsUnique();
            b.HasIndex(c => c.CarpetaId);
        });

        modelBuilder.Entity<CorreoDescartadoRegistro>(b =>
        {
            b.ToTable("CorreosDescartados");
            b.HasKey(c => c.Id);
            b.Property(c => c.MessageId).HasMaxLength(CorreoDescartadoRegistro.MaxMessageIdLength).IsRequired();
            b.Property(c => c.Asunto).HasMaxLength(CorreoDescartadoRegistro.MaxAsuntoLength).IsRequired();
            b.Property(c => c.Remitente).HasMaxLength(CorreoDescartadoRegistro.MaxRemitenteLength).IsRequired();
            b.Property(c => c.Motivo).HasMaxLength(CorreoDescartadoRegistro.MaxMotivoLength).IsRequired();
            // One row per message: syncing again never duplicates it.
            b.HasIndex(c => c.MessageId).IsUnique();
            b.HasIndex(c => c.DescartadoEn);
        });

        modelBuilder.Entity<EnvioCorreoComuna>(b =>
        {
            b.ToTable("EnviosCorreoComuna");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever().UseCollation("NOCASE");
            b.Property(e => e.Tipo).HasConversion<string>().HasMaxLength(30);
            b.Property(e => e.Comuna).HasMaxLength(EnvioCorreoComuna.MaxComunaLength).IsRequired();
            b.Property(e => e.Destinatarios).HasMaxLength(EnvioCorreoComuna.MaxDestinatariosLength).IsRequired();
            b.Property(e => e.Asunto).HasMaxLength(EnvioCorreoComuna.MaxAsuntoLength).IsRequired();
            b.Property(e => e.Cuerpo).HasMaxLength(EnvioCorreoComuna.MaxCuerpoLength).IsRequired();
            b.Property(e => e.Autor).HasMaxLength(EnvioCorreoComuna.MaxAutorLength).IsRequired();
            b.Property(e => e.Estado).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.Error).HasMaxLength(EnvioCorreoComuna.MaxErrorLength);
            b.Property(e => e.Nota).HasMaxLength(EnvioCorreoComuna.MaxNotaLength);
            b.Property(e => e.PantallazoArchivo).HasMaxLength(EnvioCorreoComuna.MaxPantallazoLength);
            b.HasMany(e => e.Carpetas).WithOne().HasForeignKey(l => l.EnvioId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(e => e.Carpetas).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(e => new { e.Tipo, e.EnviadoEn });
            b.HasIndex(e => e.Estado);
        });

        modelBuilder.Entity<EnvioCorreoComunaCarpeta>(b =>
        {
            b.ToTable("EnviosCorreoComunaCarpetas");
            b.HasKey(l => new { l.EnvioId, l.CarpetaId });
            b.Property(l => l.EnvioId).UseCollation("NOCASE");
            b.Property(l => l.CarpetaId).UseCollation("NOCASE");
            b.HasOne<Carpeta>().WithMany().HasForeignKey(l => l.CarpetaId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(l => l.CarpetaId);
        });

        modelBuilder.Entity<PlantillaCorreo>(b =>
        {
            b.ToTable("PlantillasCorreo");
            b.HasKey(p => p.Clave);
            b.Property(p => p.Clave).HasMaxLength(PlantillaCorreo.MaxClaveLength);
            b.Property(p => p.Asunto).HasMaxLength(PlantillaCorreo.MaxAsuntoLength).IsRequired();
            b.Property(p => p.Cuerpo).HasMaxLength(PlantillaCorreo.MaxCuerpoLength).IsRequired();
        });
    }
}
