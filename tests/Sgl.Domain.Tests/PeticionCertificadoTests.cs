using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>"Pedir": a carpeta in Crear certificado / oficio is requested from Dirección and Secretaría (audited).</summary>
public class PeticionCertificadoTests
{
    private static readonly DateTime Pedida = new(2026, 3, 11, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RegistrarPeticion_records_the_date_and_audits()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.CrearCertificado);

        c.RegistrarPeticion("javiera", Pedida);

        Assert.Equal(Pedida, c.FechaPeticion);
        Assert.Contains(c.Historial, h => h.Campo == "FechaPeticion" && h.Anterior is null && h.Usuario == "javiera");
    }

    [Fact]
    public void RegistrarPeticion_again_restamps_and_audits_the_new_request()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.CrearCertificado);
        c.RegistrarPeticion("javiera", Pedida);
        var segunda = Pedida.AddDays(2);

        c.RegistrarPeticion("ana", segunda);

        Assert.Equal(segunda, c.FechaPeticion);
        Assert.Equal(2, c.Historial.Count(h => h.Campo == "FechaPeticion"));
    }

    [Theory]
    [InlineData(EstadoCarpeta.Citada)]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.SeEncuentraEnArchivos)]
    public void RegistrarPeticion_is_rejected_outside_Crear_certificado(EstadoCarpeta estado)
    {
        var c = CarpetaFactory.InEstado(estado);

        Assert.Throws<DomainException>(() => c.RegistrarPeticion("javiera", Pedida));
        Assert.Null(c.FechaPeticion);
    }
}
