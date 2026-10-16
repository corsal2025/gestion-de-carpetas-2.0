using PeticionCambioDomicilio.Data;
using PeticionCambioDomicilio.Domain;

namespace PeticionCambioDomicilio.Tests;

public sealed class PeticionRepositoryBackupTests
{
    [Fact]
    public void BackupGuardaEnLaCarpetaElegidaYConservaLosDatosConLaBaseAbierta()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var backupDirectory = Path.Combine(directory, "respaldo");
        Directory.CreateDirectory(directory);

        try
        {
            var dbPath = Path.Combine(directory, "data", "peticiones.db");
            using var repository = new PeticionRepository(dbPath, backupDirectory);
            var petition = new Peticion
            {
                NombreCompleto = "Persona de respaldo",
                Rut = "77777777-7",
                Comuna = "Valparaíso",
                CreadaEn = DateTimeOffset.Parse("2026-10-01T12:00:00+00:00"),
            };
            repository.AddIfNew(petition);

            var backupPath = repository.Backup("manual");

            Assert.NotNull(backupPath);
            Assert.Equal(Path.GetFullPath(backupDirectory), Path.GetDirectoryName(Path.GetFullPath(backupPath)));
            using var backupRepository = new PeticionRepository(backupPath);
            var backedUpPetition = Assert.Single(backupRepository.All());
            Assert.Equal(petition.Rut, backedUpPetition.Rut);
            Assert.Equal(petition.NombreCompleto, backedUpPetition.NombreCompleto);

            var dailyBackup = repository.BackupDiario();
            Assert.NotNull(dailyBackup);
            Assert.Equal(dailyBackup, repository.BackupDiario());
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // SQLite puede conservar temporalmente archivos auxiliares en Windows.
            }
        }
    }
}