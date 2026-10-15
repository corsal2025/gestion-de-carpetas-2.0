using Sgl.Application.Carpetas;

namespace Sgl.Application.Tests;

public sealed class OrdenCacheadoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);

    private static OrdenCacheado Cargado(long version = 7, int cantidad = 3)
    {
        var cache = new OrdenCacheado(TimeSpan.FromSeconds(5));
        Assert.True(cache.IniciarCarga(out var generacion));
        var ids = Enumerable.Range(0, cantidad).Select(_ => Guid.NewGuid()).ToList();
        Assert.True(cache.Completar(generacion, ids, version, T0));
        return cache;
    }

    [Fact]
    public void Starts_empty_and_allows_only_one_load_at_a_time()
    {
        var cache = new OrdenCacheado();

        Assert.False(cache.Tiene);
        Assert.True(cache.IniciarCarga(out _));
        Assert.False(cache.IniciarCarga(out _));
    }

    [Fact]
    public void Completar_is_ignored_when_the_cache_was_invalidated_meanwhile()
    {
        var cache = new OrdenCacheado();
        cache.IniciarCarga(out var generacion);

        cache.Invalidar();
        var aceptado = cache.Completar(generacion, [Guid.NewGuid()], 1, T0);

        Assert.False(aceptado);
        Assert.False(cache.Tiene);
    }

    [Fact]
    public void Invalidar_drops_the_ids_and_allows_a_new_load()
    {
        var cache = Cargado();

        cache.Invalidar();

        Assert.False(cache.Tiene);
        Assert.True(cache.IniciarCarga(out _));
    }

    [Fact]
    public void DebeVerificar_only_after_the_interval_elapsed()
    {
        var cache = Cargado();

        Assert.False(cache.DebeVerificar(T0.AddSeconds(4)));
        Assert.True(cache.DebeVerificar(T0.AddSeconds(5)));
    }

    [Fact]
    public void DebeVerificar_is_false_without_cached_ids()
    {
        Assert.False(new OrdenCacheado().DebeVerificar(T0.AddMinutes(10)));
    }

    [Fact]
    public void Verificar_keeps_the_cache_when_the_version_is_unchanged_and_restarts_the_interval()
    {
        var cache = Cargado(version: 7);

        var vigente = cache.Verificar(7, T0.AddSeconds(6));

        Assert.True(vigente);
        Assert.True(cache.Tiene);
        Assert.False(cache.DebeVerificar(T0.AddSeconds(10)));
        Assert.True(cache.DebeVerificar(T0.AddSeconds(11)));
    }

    [Fact]
    public void Verificar_invalidates_when_another_circuit_changed_the_data()
    {
        var cache = Cargado(version: 7);

        var vigente = cache.Verificar(8, T0.AddSeconds(6));

        Assert.False(vigente);
        Assert.False(cache.Tiene);
    }

    [Theory]
    [InlineData(20, 20, true)]
    [InlineData(20, 19, false)]
    [InlineData(20, 0, false)]
    public void ValidarVentana_invalidates_when_a_requested_row_is_gone(int esperadas, int recibidas, bool integra)
    {
        var cache = Cargado();

        var resultado = cache.ValidarVentana(esperadas, recibidas);

        Assert.Equal(integra, resultado);
        Assert.Equal(integra, cache.Tiene);
    }
}
