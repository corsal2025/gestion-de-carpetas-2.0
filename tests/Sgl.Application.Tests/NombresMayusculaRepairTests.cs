using Microsoft.EntityFrameworkCore;
using Sgl.Application.Carpetas;
using Sgl.Domain;
using Sgl.Infrastructure.Persistence;

namespace Sgl.Application.Tests;

public sealed class NombresMayusculaRepairTests
{
    private static async Task<Guid> SeedLegacyAsync(TestDb db, string rut, string nombre, string apellido)
    {
        var carpeta = await db.NewService().RegistrarAsync(new RegistrarCarpetaCommand(rut, "X", "Y", Sede.AvArgentina, new DateOnly(2026, 5, 1), "ana"));
        using var ctx = db.NewContext();
        var ciudadanoId = await ctx.Carpetas.Where(c => c.Id == carpeta).Select(c => c.CiudadanoId).SingleAsync();
        // Legacy rows were written before names were normalized: bypass the domain on purpose.
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE Ciudadanos SET Nombre = {0}, Apellido = {1}, NombreBusqueda = {2} WHERE upper(Id) = upper({3})",
            nombre, apellido, Ciudadano.NormalizeForSearch($"{nombre} {apellido}"), ciudadanoId.ToString());
        return ciudadanoId;
    }

    [Fact]
    public async Task Uppercases_legacy_names_splits_vecino_and_is_idempotent()
    {
        using var db = new TestDb();
        var vecino = await SeedLegacyAsync(db, "11111111-1", "María Aidée Pradenas Tiznado", "Vecino");
        var mixto = await SeedLegacyAsync(db, "22222222-2", "Mauricio", "Salazar");
        var limpio = await SeedLegacyAsync(db, "33333333-3", "BRYAM", "BRAVO");

        var pendientesAntes = await NombresMayusculaRepair.PendientesAsync(db.NewContext());
        var primera = await NombresMayusculaRepair.RepararAsync(db.NewContext());
        var segunda = await NombresMayusculaRepair.RepararAsync(db.NewContext());
        var pendientesDespues = await NombresMayusculaRepair.PendientesAsync(db.NewContext());

        using var ctx = db.NewContext();
        var c = await ctx.Ciudadanos.ToDictionaryAsync(x => x.Id);
        Assert.Equal(("MARÍA AIDÉE", "PRADENAS TIZNADO"), (c[vecino].Nombre, c[vecino].Apellido));
        Assert.Equal("maria aidee pradenas tiznado", c[vecino].NombreBusqueda);
        Assert.Equal(("MAURICIO", "SALAZAR"), (c[mixto].Nombre, c[mixto].Apellido));
        Assert.Equal(("BRYAM", "BRAVO"), (c[limpio].Nombre, c[limpio].Apellido));
        Assert.Equal(2, pendientesAntes);
        Assert.Equal(2, primera);
        Assert.Equal(0, segunda);
        Assert.Equal(0, pendientesDespues);
    }
}
