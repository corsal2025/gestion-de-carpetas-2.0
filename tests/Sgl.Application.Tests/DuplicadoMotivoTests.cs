using Sgl.Application.Carpetas;

namespace Sgl.Application.Tests;

public sealed class DuplicadoMotivoTests
{
    private static readonly DateOnly Dia = new(2026, 10, 11);

    [Fact]
    public void Describir_flags_an_invalid_rut_before_anything_else()
    {
        var motivo = DuplicadoMotivo.Describir(rutValido: false, Dia, Dia, mismaSede: true);

        Assert.Equal("RUT inválido: revisar los datos del ciudadano", motivo);
    }

    [Fact]
    public void Describir_reports_the_same_appointment_loaded_twice()
    {
        var motivo = DuplicadoMotivo.Describir(rutValido: true, Dia, Dia, mismaSede: true);

        Assert.Equal("Misma cita repetida (11-10-2026)", motivo);
    }

    [Fact]
    public void Describir_reports_same_day_in_different_sedes()
    {
        var motivo = DuplicadoMotivo.Describir(rutValido: true, Dia, Dia, mismaSede: false);

        Assert.Equal("Misma fecha en sedes distintas (11-10-2026)", motivo);
    }

    [Fact]
    public void Describir_reports_different_dates_in_the_same_sede()
    {
        var motivo = DuplicadoMotivo.Describir(rutValido: true, Dia, Dia.AddDays(5), mismaSede: true);

        Assert.Equal("Citas en fechas distintas (11-10-2026 a 16-10-2026)", motivo);
    }

    [Fact]
    public void Describir_reports_different_dates_and_sedes()
    {
        var motivo = DuplicadoMotivo.Describir(rutValido: true, Dia, Dia.AddDays(5), mismaSede: false);

        Assert.Equal("Fechas y sedes distintas (11-10-2026 a 16-10-2026)", motivo);
    }
}
