using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class SectorFisicoParseTests
{
    [Theory]
    [InlineData("30-06-2023", SectorFisico.Archivo)]
    [InlineData("01-07-2023", SectorFisico.Oficina43)]
    [InlineData("02-07-2023", SectorFisico.Oficina43)] // dd-MM-yyyy, never read as 7 February
    [InlineData("12-01-2024", SectorFisico.Oficina43)] // 12 January, never 1 December
    [InlineData("2023-06-30", SectorFisico.Archivo)]
    [InlineData("2023-07-01", SectorFisico.Oficina43)]
    [InlineData("30/06/2023", SectorFisico.Archivo)]
    [InlineData("1 de julio de 2023", SectorFisico.Oficina43)]
    [InlineData("15 marzo 2019", SectorFisico.Archivo)]
    [InlineData("", SectorFisico.Desconocido)]
    [InlineData(null, SectorFisico.Desconocido)]
    [InlineData("no es fecha", SectorFisico.Desconocido)]
    public void Calcular_reads_chilean_day_first_dates(string? entrada, string esperado)
    {
        Assert.Equal(esperado, SectorFisico.Calcular(entrada));
    }

    [Theory]
    [InlineData("02-07-2023", "2023-07-02")]
    [InlineData("2023-07-02", "2023-07-02")]
    [InlineData("1 de julio de 2023", "2023-07-01")]
    public void NormalizarIso_returns_iso_for_a_recognized_date(string entrada, string esperado)
    {
        Assert.Equal(esperado, SectorFisico.NormalizarIso(entrada));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("quilpue")]
    public void NormalizarIso_returns_null_when_it_is_not_a_date(string? entrada)
    {
        Assert.Null(SectorFisico.NormalizarIso(entrada));
    }
}
