using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>
/// ENTRADA keeps only clean carpetas ("Licencias para clasificar"); every carpeta with a problem goes to exactly one
/// REVISIÓN submodule, the first that matches in the order Duplicadas, RUT inválido, Sin RUT, Licencia no identificada,
/// No evaluadas, Citadas.
/// </summary>
public class RevisionEntradaTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 17);
    private const string RutValido = "11111111-1";
    private const string RutDvMalo = "11111111-2";

    private static SenalesRevision Limpia(
        bool duplicada = false, string? rut = RutValido, TramiteAgenda? tramite = TramiteAgenda.Renovacion, DateOnly? cita = null) =>
        new(duplicada, rut, tramite, cita ?? Hoy);

    [Fact]
    public void Clean_agenda_carpeta_stays_in_Entrantes_para_clasificar()
    {
        Assert.Equal(Bandeja.PorClasificar, RevisionEntrada.Clasificar(Bandeja.PorClasificar, Limpia(), Hoy));
    }

    [Fact]
    public void Clean_citada_goes_to_Citadas()
    {
        Assert.Equal(Bandeja.ParaRevision, RevisionEntrada.Clasificar(Bandeja.ParaRevision, Limpia(tramite: null), Hoy));
    }

    [Theory]
    [InlineData(Bandeja.PorClasificar)]
    [InlineData(Bandeja.ParaRevision)]
    public void Duplicate_goes_to_Duplicadas(Bandeja origen)
    {
        Assert.Equal(Bandeja.RevisionDuplicadas, RevisionEntrada.Clasificar(origen, Limpia(duplicada: true), Hoy));
    }

    [Theory]
    [InlineData(RutDvMalo)]
    [InlineData("ABC-1")]
    [InlineData("12.345.678-0")]
    public void Present_but_invalid_rut_goes_to_Rut_invalido(string rut)
    {
        Assert.Equal(Bandeja.RevisionRutInvalido, RevisionEntrada.Clasificar(Bandeja.PorClasificar, Limpia(rut: rut), Hoy));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_rut_goes_to_Sin_rut(string? rut)
    {
        Assert.Equal(Bandeja.RevisionSinRut, RevisionEntrada.Clasificar(Bandeja.ParaRevision, Limpia(rut: rut), Hoy));
    }

    [Fact]
    public void Agenda_carpeta_with_unknown_tramite_goes_to_Licencia_no_identificada()
    {
        Assert.Equal(Bandeja.RevisionNoIdentificada, RevisionEntrada.Clasificar(Bandeja.PorClasificar, Limpia(tramite: null), Hoy));
    }

    [Fact]
    public void Citada_without_agenda_tramite_is_not_unidentified()
    {
        // Excel (legacy) Citadas never carry an agenda trámite: that is not a problem for them.
        Assert.Equal(Bandeja.ParaRevision, RevisionEntrada.Clasificar(Bandeja.ParaRevision, Limpia(tramite: null), Hoy));
    }

    [Theory]
    [InlineData(Bandeja.PorClasificar, Bandeja.RevisionNoEvaluadas)]
    [InlineData(Bandeja.ParaRevision, Bandeja.RevisionNoEvaluadas)]
    public void Appointment_still_in_the_future_goes_to_No_evaluadas(Bandeja origen, Bandeja esperada)
    {
        Assert.Equal(esperada, RevisionEntrada.Clasificar(origen, Limpia(cita: Hoy.AddDays(1)), Hoy));
    }

    [Fact]
    public void Appointment_today_counts_as_evaluated()
    {
        Assert.Equal(Bandeja.PorClasificar, RevisionEntrada.Clasificar(Bandeja.PorClasificar, Limpia(cita: Hoy), Hoy));
    }

    [Fact]
    public void First_matching_problem_wins_in_order_a_to_f()
    {
        var todo = new SenalesRevision(true, RutDvMalo, null, Hoy.AddDays(3));
        Assert.Equal(Bandeja.RevisionDuplicadas, RevisionEntrada.Clasificar(Bandeja.PorClasificar, todo, Hoy));
        Assert.Equal(Bandeja.RevisionRutInvalido, RevisionEntrada.Clasificar(Bandeja.PorClasificar, todo with { Duplicada = false }, Hoy));
        Assert.Equal(Bandeja.RevisionSinRut, RevisionEntrada.Clasificar(Bandeja.PorClasificar, todo with { Duplicada = false, Rut = null }, Hoy));
        Assert.Equal(Bandeja.RevisionNoIdentificada, RevisionEntrada.Clasificar(Bandeja.PorClasificar, todo with { Duplicada = false, Rut = RutValido }, Hoy));
        Assert.Equal(Bandeja.RevisionNoEvaluadas,
            RevisionEntrada.Clasificar(Bandeja.PorClasificar, todo with { Duplicada = false, Rut = RutValido, TramiteAgenda = TramiteAgenda.Renovacion }, Hoy));
    }

    [Theory]
    [InlineData(Bandeja.PorPreparar)]
    [InlineData(Bandeja.Archivadas)]
    [InlineData(Bandeja.SubidaConaset)]
    public void Trays_outside_Entrada_are_never_touched(Bandeja otra)
    {
        Assert.Equal(otra, RevisionEntrada.Clasificar(otra, new SenalesRevision(true, null, null, Hoy.AddDays(9)), Hoy));
    }

    [Fact]
    public void Submodules_are_listed_in_menu_order_and_end_with_Citadas()
    {
        Assert.Equal(
            [Bandeja.RevisionDuplicadas, Bandeja.RevisionRutInvalido, Bandeja.RevisionSinRut, Bandeja.RevisionNoIdentificada, Bandeja.RevisionNoEvaluadas, Bandeja.ParaRevision],
            RevisionEntrada.Submodulos);
    }

    [Theory]
    [InlineData(Bandeja.PorClasificar, true)]
    [InlineData(Bandeja.ParaRevision, true)]
    [InlineData(Bandeja.RevisionSinRut, true)]
    [InlineData(Bandeja.PorPreparar, false)]
    public void EsDeEntrada_covers_Entrantes_and_every_Revision_submodule(Bandeja b, bool esperado)
    {
        Assert.Equal(esperado, RevisionEntrada.EsDeEntrada(b));
    }
}
