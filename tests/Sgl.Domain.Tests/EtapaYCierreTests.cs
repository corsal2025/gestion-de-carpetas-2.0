using Sgl.Domain;

namespace Sgl.Domain.Tests;

/// <summary>R7: automatic stage date (Chile time), FechaSubida only when empty, FechaCierre never invented.</summary>
public class EtapaYCierreTests
{
    private static readonly DateTime Alta = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Despues = new(2026, 3, 12, 15, 30, 0, DateTimeKind.Utc);

    private static Carpeta Agenda() =>
        Carpeta.CrearDesdeAgenda(new Ciudadano(Rut.Parse("12345678-5"), "Juan", "Pérez"),
            Sede.Placilla, new DateOnly(2026, 3, 10), null, TramiteAgenda.Renovacion, "importador", Alta);

    [Fact]
    public void New_carpetas_start_their_stage_when_they_enter_the_system()
    {
        Assert.Equal(CarpetaFactory.Now, CarpetaFactory.New().FechaEstadoActual);
        Assert.Equal(Alta, Agenda().FechaEstadoActual);
    }

    [Fact]
    public void CrearImportada_uses_the_import_time_as_stage_date_and_invents_no_closing_date()
    {
        var c = Carpeta.CrearImportada(Guid.NewGuid(), new Ciudadano(Rut.Parse("12345678-5"), "Juan", "Pérez"),
            Sede.AvArgentina, new DateOnly(2026, 1, 5), null, null, EstadoCarpeta.SubidaConaset, Decision.Pendiente, null, null, null, null, Alta);

        Assert.Equal(Alta, c.FechaEstadoActual);
        Assert.Null(c.FechaSubida);
        Assert.Null(c.FechaCierre);
    }

    [Fact]
    public void CrearImportada_closing_date_comes_only_from_the_excel_upload_date()
    {
        var c = Carpeta.CrearImportada(Guid.NewGuid(), new Ciudadano(Rut.Parse("12345678-5"), "Juan", "Pérez"),
            Sede.AvArgentina, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), null, EstadoCarpeta.SubidaConaset, Decision.Pendiente, null, null, null, null, Alta);

        Assert.Equal(new DateOnly(2026, 1, 9), c.FechaCierre);
    }

    [Fact]
    public void Every_state_change_restamps_the_stage_date()
    {
        var c = Agenda();

        c.Clasificar(EstadoCarpeta.SeEncuentraEnArchivos, ClaseLicencia.B, "2019-01-01", null, "javiera", Despues);

        Assert.Equal(Despues, c.FechaEstadoActual);
    }

    [Fact]
    public void ForzarEstado_to_another_state_restamps_and_to_the_same_state_does_not()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);

        c.ForzarEstado(EstadoCarpeta.SeEncuentraEnArchivos, "ana", Despues);
        Assert.Equal(CarpetaFactory.Now, c.FechaEstadoActual);

        c.ForzarEstado(EstadoCarpeta.NoExisteCarpeta, "ana", Despues);
        Assert.Equal(Despues, c.FechaEstadoActual);
    }

    [Fact]
    public void Non_state_edits_do_not_touch_the_stage_date()
    {
        var c = CarpetaFactory.InEstado(EstadoCarpeta.SeEncuentraEnArchivos);

        c.ModificarObservacion("nota", "ana", Despues);
        c.ModificarFechaUltimaCarpeta("2019-01-01", "ana", Despues);

        Assert.Equal(CarpetaFactory.Now, c.FechaEstadoActual);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SubidaConaset)]
    [InlineData(EstadoCarpeta.SubidaConF8)]
    [InlineData(EstadoCarpeta.SubidaConOficio)]
    [InlineData(EstadoCarpeta.CambioDomSubidoConaset)]
    [InlineData(EstadoCarpeta.CambioDomSubidoCorreo)]
    [InlineData(EstadoCarpeta.CrearCertificado)]
    public void Closing_states_stamp_the_closing_date_in_chile_time(EstadoCarpeta cierre)
    {
        var c = Agenda();
        // Chile date, not UTC: 2026-03-13 02:00 UTC is still 2026-03-12 in Chile.
        var noche = new DateTime(2026, 3, 13, 2, 0, 0, DateTimeKind.Utc);

        c.ForzarEstado(cierre, "ana", noche);

        Assert.Equal(new DateOnly(2026, 3, 12), c.FechaCierre);
    }

    [Theory]
    [InlineData(EstadoCarpeta.SubidaConaset)]
    [InlineData(EstadoCarpeta.SubidaConF8)]
    [InlineData(EstadoCarpeta.SubidaConOficio)]
    public void Upload_states_stamp_FechaSubida_when_empty(EstadoCarpeta subida)
    {
        var c = Agenda();

        c.ForzarEstado(subida, "ana", Despues);

        Assert.Equal(new DateOnly(2026, 3, 12), c.FechaSubida);
    }

    [Fact]
    public void An_existing_FechaSubida_is_never_overwritten_by_a_state_change()
    {
        var c = Agenda();
        c.Editar(c.Sede, c.FechaCitacion, new DateOnly(2026, 3, 11), null, "ana", Alta);

        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Despues);

        Assert.Equal(new DateOnly(2026, 3, 11), c.FechaSubida);
        Assert.Equal(new DateOnly(2026, 3, 12), c.FechaCierre);
    }

    [Fact]
    public void Non_closing_states_never_invent_dates()
    {
        var c = Agenda();

        c.ForzarEstado(EstadoCarpeta.SeEncuentraEnOf43, "ana", Despues);
        c.ForzarEstado(EstadoCarpeta.NoExisteCarpeta, "ana", Despues);

        Assert.Null(c.FechaSubida);
        Assert.Null(c.FechaCierre);
    }

    [Fact]
    public void Leaving_a_closing_state_for_a_search_state_reopens_the_process()
    {
        var c = Agenda();
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Alta);
        Assert.NotNull(c.FechaCierre);

        c.ForzarEstado(EstadoCarpeta.SeEncuentraEnArchivos, "ana", Despues);

        Assert.Null(c.FechaCierre);
        Assert.Contains(c.Historial, h => h.Campo == "FechaCierre" && h.Nuevo is null);
    }

    [Fact]
    public void Moving_between_two_closing_states_keeps_the_first_closing_date()
    {
        var c = Agenda();
        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Alta);

        c.ForzarEstado(EstadoCarpeta.SubidaConF8, "ana", Despues);

        Assert.Equal(new DateOnly(2026, 3, 10), c.FechaCierre);
    }

    [Fact]
    public void A_final_decision_also_records_the_closing_date()
    {
        var c = CarpetaFactory.New();

        c.CambiarDecision(Decision.Otorgado, "ana", Despues);

        Assert.Equal(new DateOnly(2026, 3, 12), c.FechaCierre);
    }

    [Fact]
    public void Closing_date_changes_are_audited()
    {
        var c = Agenda();

        c.ForzarEstado(EstadoCarpeta.SubidaConaset, "ana", Despues);

        Assert.Contains(c.Historial, h => h.Campo == "FechaCierre" && h.Anterior is null && h.Nuevo == "2026-03-12");
    }
}
