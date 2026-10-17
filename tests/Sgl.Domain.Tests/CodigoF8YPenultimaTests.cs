using Sgl.Domain;

namespace Sgl.Domain.Tests;

public class CodigoF8YPenultimaTests
{
    private static readonly DateTime Now = CarpetaFactory.Now; // 2026-03-10 12:00 UTC

    [Fact]
    public void CodigoF8_is_trimmed_and_audited()
    {
        var c = CarpetaFactory.New();

        c.AsignarCodigoF8("  0123456789 ", "ana", Now);

        Assert.Equal("0123456789", c.CodigoF8);
        var h = Assert.Single(c.Historial, x => x.Campo == nameof(Carpeta.CodigoF8));
        Assert.Null(h.Anterior);
        Assert.Equal("0123456789", h.Nuevo);
        Assert.Equal("ana", h.Usuario);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CodigoF8_empty_clears_to_null_with_history(string? vacio)
    {
        var c = CarpetaFactory.New();
        c.AsignarCodigoF8("81", "ana", Now);

        c.AsignarCodigoF8(vacio, "ana", Now);

        Assert.Null(c.CodigoF8);
        Assert.Equal(2, c.Historial.Count(x => x.Campo == nameof(Carpeta.CodigoF8)));
    }

    [Fact]
    public void CodigoF8_unchanged_or_empty_over_empty_writes_no_history()
    {
        var c = CarpetaFactory.New();
        c.AsignarCodigoF8("81", "ana", Now);
        var antes = c.Historial.Count;

        c.AsignarCodigoF8(" 81 ", "ana", Now);
        Assert.Equal(antes, c.Historial.Count);

        var vacia = CarpetaFactory.New();
        var n = vacia.Historial.Count;
        vacia.AsignarCodigoF8(null, "ana", Now);
        Assert.Equal(n, vacia.Historial.Count);
    }

    [Theory]
    [InlineData("F8-1")]
    [InlineData("12 34")]
    [InlineData("12a")]
    [InlineData("1.234")]
    [InlineData("-12")]
    [InlineData("١٢٣")]
    public void CodigoF8_with_non_digit_characters_is_rejected(string codigo)
    {
        var c = CarpetaFactory.New();

        var ex = Assert.Throws<DomainException>(() => c.AsignarCodigoF8(codigo, "ana", Now));

        Assert.Contains("solo admite números", ex.Message);
        Assert.Null(c.CodigoF8);
    }

    [Fact]
    public void CodigoF8_max_length_is_15_digits()
    {
        Assert.Equal(15, Carpeta.MaxCodigoF8Length);
    }

    [Fact]
    public void CodigoF8_over_max_length_is_rejected_and_exactly_max_is_accepted()
    {
        var c = CarpetaFactory.New();

        Assert.Throws<DomainException>(() => c.AsignarCodigoF8(new string('1', Carpeta.MaxCodigoF8Length + 1), "ana", Now));
        Assert.Null(c.CodigoF8);

        c.AsignarCodigoF8(new string('1', Carpeta.MaxCodigoF8Length), "ana", Now);
        Assert.Equal(Carpeta.MaxCodigoF8Length, c.CodigoF8!.Length);
    }

    [Fact]
    public void CodigoF8_requires_an_author_and_an_active_carpeta()
    {
        var c = CarpetaFactory.New();
        Assert.Throws<DomainException>(() => c.AsignarCodigoF8("11", " ", Now));

        c.Anular("duplicada", "ana", Now);
        Assert.Throws<DomainException>(() => c.AsignarCodigoF8("11", "ana", Now));
        Assert.Null(c.CodigoF8);
    }

    [Fact]
    public void FechaPenultima_is_stored_and_audited_in_iso()
    {
        var c = CarpetaFactory.New();

        c.AsignarFechaPenultimaCarpeta(new DateOnly(2019, 5, 2), "ana", Now);

        Assert.Equal(new DateOnly(2019, 5, 2), c.FechaPenultimaCarpeta);
        var h = Assert.Single(c.Historial, x => x.Campo == nameof(Carpeta.FechaPenultimaCarpeta));
        Assert.Null(h.Anterior);
        Assert.Equal("2019-05-02", h.Nuevo);
    }

    [Fact]
    public void FechaPenultima_may_precede_the_citation_date_and_can_be_today_in_Chile()
    {
        var c = CarpetaFactory.New();
        c.AsignarFechaPenultimaCarpeta(c.FechaCitacion.AddYears(-5), "ana", Now);
        Assert.NotNull(c.FechaPenultimaCarpeta);

        c.AsignarFechaPenultimaCarpeta(HoraChile.Fecha(Now), "ana", Now);
        Assert.Equal(HoraChile.Fecha(Now), c.FechaPenultimaCarpeta);
    }

    [Fact]
    public void FechaPenultima_in_the_future_is_rejected_using_the_Chile_date()
    {
        var c = CarpetaFactory.New();
        var manana = HoraChile.Fecha(Now).AddDays(1);

        Assert.Throws<DomainException>(() => c.AsignarFechaPenultimaCarpeta(manana, "ana", Now));
        Assert.Null(c.FechaPenultimaCarpeta);

        // 01:30 UTC is still the previous evening in Chile: that Chile "today" is the limit, not the UTC date.
        var tarde = new DateTime(2026, 3, 11, 1, 30, 0, DateTimeKind.Utc);
        var hoyChile = HoraChile.Fecha(tarde);
        Assert.Equal(new DateOnly(2026, 3, 10), hoyChile);
        Assert.Throws<DomainException>(() => c.AsignarFechaPenultimaCarpeta(new DateOnly(2026, 3, 11), "ana", tarde));
        c.AsignarFechaPenultimaCarpeta(hoyChile, "ana", tarde);
    }

    [Fact]
    public void FechaPenultima_null_clears_and_unchanged_writes_no_history()
    {
        var c = CarpetaFactory.New();
        c.AsignarFechaPenultimaCarpeta(new DateOnly(2019, 5, 2), "ana", Now);
        var antes = c.Historial.Count;

        c.AsignarFechaPenultimaCarpeta(new DateOnly(2019, 5, 2), "ana", Now);
        Assert.Equal(antes, c.Historial.Count);

        c.AsignarFechaPenultimaCarpeta(null, "ana", Now);
        Assert.Null(c.FechaPenultimaCarpeta);
        Assert.Equal(antes + 1, c.Historial.Count);
        Assert.Equal("2019-05-02", c.Historial.Last(x => x.Campo == nameof(Carpeta.FechaPenultimaCarpeta)).Anterior);
    }

    [Fact]
    public void FechaPenultima_requires_an_author_and_an_active_carpeta()
    {
        var c = CarpetaFactory.New();
        Assert.Throws<DomainException>(() => c.AsignarFechaPenultimaCarpeta(new DateOnly(2019, 5, 2), "", Now));

        c.Anular("duplicada", "ana", Now);
        Assert.Throws<DomainException>(() => c.AsignarFechaPenultimaCarpeta(new DateOnly(2019, 5, 2), "ana", Now));
    }

    [Theory]
    [InlineData(EstadoCarpeta.NoExisteCarpeta)]
    [InlineData(EstadoCarpeta.Citada)]
    [InlineData(EstadoCarpeta.Revision)]
    public void Both_fields_are_editable_in_any_estado(EstadoCarpeta estado)
    {
        var c = CarpetaFactory.InEstado(estado);

        c.AsignarCodigoF8("9", "ana", Now);
        c.AsignarFechaPenultimaCarpeta(new DateOnly(2018, 1, 1), "ana", Now);

        Assert.Equal("9", c.CodigoF8);
        Assert.Equal(estado, c.Estado);
    }
}
