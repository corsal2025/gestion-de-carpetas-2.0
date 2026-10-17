using Sgl.Application.Integridad;
using Sgl.Domain;

namespace Sgl.Application.Tests;

/// <summary>R12: one test group per rule, over synthetic snapshots (no database).</summary>
public sealed class IntegridadReglasTests
{
    private static readonly DateOnly Hoy = new(2026, 5, 4);
    private static readonly DateTime Registro = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private static CarpetaIntegridad C(
        EstadoCarpeta estado = EstadoCarpeta.SeEncuentraEnArchivos,
        Guid? ciudadano = null,
        ClaseLicencia clases = ClaseLicencia.B,
        Decision decision = Decision.Pendiente,
        bool anulada = false,
        string? caja = null,
        DateOnly? citacion = null,
        DateOnly? subida = null,
        DateOnly? cierre = null,
        DateTime? registro = null,
        bool pedida = false) =>
        new(Guid.NewGuid(), ciudadano ?? Guid.NewGuid(), "11111111-1", estado, decision, caja, clases, anulada,
            citacion ?? new DateOnly(2026, 4, 1), subida, cierre, registro ?? Registro, pedida);

    /// <summary>A consistent snapshot: each active carpeta in the tray its state dictates, counters matching.</summary>
    private static EntradaIntegridad Coherente(
        IReadOnlyList<CarpetaIntegridad> carpetas,
        IReadOnlyDictionary<Guid, DateTime>? revisiones = null)
    {
        var visibles = carpetas.Where(c => !c.Anulada)
            .ToDictionary(c => c.Id, c => (IReadOnlyList<Bandeja>)[Carpeta.ClasificarBandeja(c.Estado, c.Decision, c.CajaArchivo, c.Pedida)]);
        var conteo = Enum.GetValues<Bandeja>().ToDictionary(b => b, b => visibles.Values.Count(v => v[0] == b));
        return new EntradaIntegridad(carpetas, visibles, conteo, conteo, carpetas.Count(c => !c.Anulada),
            revisiones ?? new Dictionary<Guid, DateTime>());
    }

    private static IReadOnlyList<ViolacionIntegridad> Evaluar(EntradaIntegridad e) => ReglasIntegridad.Evaluar(e, Hoy);

    [Fact]
    public void A_requested_certificate_in_En_certificado_is_consistent_and_in_the_old_tray_is_not()
    {
        var pedida = C(EstadoCarpeta.CrearCertificado, pedida: true);
        var coherente = Coherente([pedida, C(EstadoCarpeta.CrearCertificado)]);
        Assert.Empty(Evaluar(coherente));

        // A screen still showing the requested carpeta in the Crear certificado tray is a violation.
        var visibles = coherente.BandejasVisibles.ToDictionary(p => p.Key, p => p.Value);
        visibles[pedida.Id] = [Bandeja.CrearCertificado];
        var roto = coherente with { BandejasVisibles = visibles };
        Assert.Contains(Evaluar(roto), v => v.Regla == ReglaIntegridad.UnaSolaBandeja && v.CarpetaId == pedida.Id);
    }

    [Fact]
    public void A_consistent_snapshot_has_no_problems()
    {
        var persona = Guid.NewGuid();
        var e = Coherente([
            C(), C(EstadoCarpeta.SubidaConaset, subida: new DateOnly(2026, 4, 5), cierre: new DateOnly(2026, 4, 5)),
            C(EstadoCarpeta.Otorgado, decision: Decision.Otorgado, cierre: new DateOnly(2026, 4, 20)),
            C(anulada: true), C(ciudadano: persona, clases: ClaseLicencia.B), C(ciudadano: persona, clases: ClaseLicencia.C),
        ]);

        Assert.Empty(Evaluar(e));
    }

    // R1 ----------------------------------------------------------------

    [Fact]
    public void R1_flags_a_carpeta_shown_in_two_trays()
    {
        var c = C();
        var e = Coherente([c]);
        var dos = new Dictionary<Guid, IReadOnlyList<Bandeja>>(e.BandejasVisibles)
        {
            [c.Id] = [Bandeja.SeEncuentraEnArchivos, Bandeja.PorPreparar],
        };

        var v = Evaluar(e with { BandejasVisibles = dos });

        Assert.Contains(v, x => x.Regla == ReglaIntegridad.UnaSolaBandeja && x.CarpetaId == c.Id && x.Detalle.Contains("2 bandejas"));
    }

    [Fact]
    public void R1_flags_a_carpeta_shown_in_no_tray_or_in_the_wrong_one()
    {
        var sin = C();
        var mal = C(EstadoCarpeta.SubidaConaset);
        var e = Coherente([sin, mal]);
        var visibles = new Dictionary<Guid, IReadOnlyList<Bandeja>>
        {
            [mal.Id] = [Bandeja.SubidaConF8],
        };

        var v = Evaluar(e with { BandejasVisibles = visibles });

        Assert.Equal(2, v.Count(x => x.Regla == ReglaIntegridad.UnaSolaBandeja));
    }

    // R2 ----------------------------------------------------------------

    [Fact]
    public void R2_flags_trays_that_do_not_add_up_to_the_total()
    {
        var e = Coherente([C(), C(), C()]);
        var conteo = new Dictionary<Bandeja, int>(e.ConteoPorBandeja) { [Bandeja.SeEncuentraEnArchivos] = 2 };

        var v = Evaluar(e with { ConteoPorBandeja = conteo, ConteoPanel = conteo });

        Assert.Contains(v, x => x.Regla == ReglaIntegridad.SumaBandejas && x.CarpetaId is null && x.Detalle.Contains("suman 2"));
    }

    [Fact]
    public void R2_flags_a_total_that_counts_cancelled_carpetas()
    {
        var e = Coherente([C(), C(anulada: true)]);

        var v = Evaluar(e with { TotalVisible = 2, ConteoPorBandeja = e.ConteoPorBandeja.ToDictionary(p => p.Key, p => p.Value + (p.Key == Bandeja.PorPreparar ? 1 : 0)) });

        Assert.Contains(v, x => x.Regla == ReglaIntegridad.SumaBandejas && x.Detalle.Contains("no anuladas"));
    }

    [Fact]
    public void R2_flags_a_tray_the_home_panel_counts_differently()
    {
        var e = Coherente([C(), C()]);
        var panel = new Dictionary<Bandeja, int>(e.ConteoPanel) { [Bandeja.SeEncuentraEnArchivos] = 1 };

        var v = Evaluar(e with { ConteoPanel = panel });

        Assert.Contains(v, x => x.Regla == ReglaIntegridad.SumaBandejas && x.Detalle.Contains("panel"));
    }

    // R3 ----------------------------------------------------------------

    [Fact]
    public void R3_flags_two_open_carpetas_of_the_same_rut_sharing_a_class()
    {
        var persona = Guid.NewGuid();
        var a = C(ciudadano: persona, clases: ClaseLicencia.B | ClaseLicencia.C);
        var b = C(EstadoCarpeta.NoExisteCarpeta, ciudadano: persona, clases: ClaseLicencia.C);

        var v = Evaluar(Coherente([a, b]));

        var ids = v.Where(x => x.Regla == ReglaIntegridad.DuplicadoMismaClase).Select(x => x.CarpetaId!.Value).ToHashSet();
        Assert.Equal(new HashSet<Guid> { a.Id, b.Id }, ids);
    }

    [Fact]
    public void R3_ignores_different_classes_closed_cancelled_archived_and_classless_carpetas()
    {
        var persona = Guid.NewGuid();
        var e = Coherente([
            C(ciudadano: persona, clases: ClaseLicencia.B),
            C(ciudadano: persona, clases: ClaseLicencia.C),
            C(EstadoCarpeta.Otorgado, ciudadano: persona, clases: ClaseLicencia.B, decision: Decision.Otorgado),
            C(ciudadano: persona, clases: ClaseLicencia.B, anulada: true),
            C(ciudadano: persona, clases: ClaseLicencia.B, caja: "CAJA AA-01"),
            C(ciudadano: persona, clases: ClaseLicencia.Ninguna),
        ]);

        Assert.DoesNotContain(Evaluar(e), x => x.Regla == ReglaIntegridad.DuplicadoMismaClase);
    }

    [Fact]
    public void R3_respects_a_review_until_a_newer_carpeta_appears()
    {
        var persona = Guid.NewGuid();
        var revisadoEn = Registro.AddDays(1);
        var a = C(ciudadano: persona, clases: ClaseLicencia.B);
        var b = C(ciudadano: persona, clases: ClaseLicencia.B);
        var revisiones = new Dictionary<Guid, DateTime> { [persona] = revisadoEn };

        Assert.DoesNotContain(Evaluar(Coherente([a, b], revisiones)), x => x.Regla == ReglaIntegridad.DuplicadoMismaClase);

        var nueva = C(ciudadano: persona, clases: ClaseLicencia.B, registro: revisadoEn.AddDays(1));
        Assert.Contains(Evaluar(Coherente([a, b, nueva], revisiones)), x => x.Regla == ReglaIntegridad.DuplicadoMismaClase);
    }

    // R4 ----------------------------------------------------------------

    [Fact]
    public void R4_flags_a_cancelled_carpeta_that_shows_in_a_tray()
    {
        var anulada = C(anulada: true);
        var e = Coherente([anulada]);
        var visibles = new Dictionary<Guid, IReadOnlyList<Bandeja>> { [anulada.Id] = [Bandeja.SeEncuentraEnArchivos] };

        var v = Evaluar(e with { BandejasVisibles = visibles });

        Assert.Contains(v, x => x.Regla == ReglaIntegridad.AnuladaVisible && x.CarpetaId == anulada.Id);
    }

    // R5 ----------------------------------------------------------------

    [Fact]
    public void R5_flags_upload_before_appointment_and_future_closing()
    {
        var antes = C(citacion: new DateOnly(2026, 4, 10), subida: new DateOnly(2026, 4, 9));
        var futura = C(EstadoCarpeta.SubidaConaset, cierre: Hoy.AddDays(1));
        var ok = C(EstadoCarpeta.SubidaConaset, subida: Hoy, cierre: Hoy);

        var v = Evaluar(Coherente([antes, futura, ok])).Where(x => x.Regla == ReglaIntegridad.FechasImposibles).ToList();

        Assert.Equal(2, v.Count);
        Assert.Contains(v, x => x.CarpetaId == antes.Id && x.Detalle.Contains("subida"));
        Assert.Contains(v, x => x.CarpetaId == futura.Id && x.Detalle.Contains("futuro"));
    }

    [Fact]
    public void Violation_details_never_carry_personal_data()
    {
        var persona = Guid.NewGuid();
        var v = Evaluar(Coherente([
            C(ciudadano: persona), C(ciudadano: persona),
            C(citacion: new DateOnly(2026, 4, 10), subida: new DateOnly(2026, 4, 1)),
        ]));

        Assert.NotEmpty(v);
        Assert.All(v, x => Assert.DoesNotContain("11111111", x.Detalle));
    }
}
