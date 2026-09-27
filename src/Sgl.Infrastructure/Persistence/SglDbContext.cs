using Microsoft.EntityFrameworkCore;
using Sgl.Domain;

namespace Sgl.Infrastructure.Persistence;

public sealed class SglDbContext(DbContextOptions<SglDbContext> options) : DbContext(options)
{
    public DbSet<Carpeta> Carpetas => Set<Carpeta>();

    public DbSet<Ciudadano> Ciudadanos => Set<Ciudadano>();

    public DbSet<HistorialCambio> Historial => Set<HistorialCambio>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ciudadano>(b =>
        {
            b.ToTable("Ciudadanos");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
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
            b.Property(c => c.Id).ValueGeneratedNever();
            b.HasOne(c => c.Ciudadano).WithMany().HasForeignKey(c => c.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
            b.Property(c => c.Sede).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Estado).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.Decision).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.IdoneidadMoral).HasMaxLength(Carpeta.MaxIdoneidadLength);
            b.HasMany(c => c.Historial).WithOne().HasForeignKey(h => h.CarpetaId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.Historial).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.HasIndex(c => c.Sede);
            b.HasIndex(c => c.Estado);
        });

        modelBuilder.Entity<HistorialCambio>(b =>
        {
            b.ToTable("HistorialCambios");
            b.HasKey(h => h.Id);
            b.Property(h => h.Usuario).HasMaxLength(Carpeta.MaxUsuarioLength).IsRequired();
            b.Property(h => h.Campo).HasMaxLength(50).IsRequired();
            b.Property(h => h.Anterior).HasMaxLength(250);
            b.Property(h => h.Nuevo).HasMaxLength(250);
        });
    }
}
