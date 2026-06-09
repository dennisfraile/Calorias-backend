using Calorias.Application.Servicios;
using Xunit;

namespace Calorias.Tests;

public class SelectorMatchUsdaTests
{
    private static CandidatoUsda C(int id, string desc, string tipo) => new(id, desc, tipo);

    [Fact]
    public void Prefiere_mayor_solape_de_tokens_con_la_query()
    {
        var cands = new[]
        {
            C(1, "Beverages, coffee substitute", "SR Legacy"),
            C(2, "Apple, raw", "Foundation"),
            C(3, "Apple pie, prepared from recipe", "SR Legacy"),
        };
        var mejor = SelectorMatchUsda.ElegirMejor(cands, "apple");
        Assert.Equal(2, mejor!.FdcId);
    }

    [Fact]
    public void Ante_igual_solape_prefiere_Foundation_sobre_SR_Legacy()
    {
        var cands = new[]
        {
            C(1, "Rice, white", "SR Legacy"),
            C(2, "Rice, white", "Foundation"),
        };
        var mejor = SelectorMatchUsda.ElegirMejor(cands, "white rice");
        Assert.Equal(2, mejor!.FdcId);
    }

    [Fact]
    public void Prefiere_descripcion_corta_cruda_sobre_preparacion_larga()
    {
        var cands = new[]
        {
            C(1, "Chicken, broilers or fryers, breast, meat only, raw", "SR Legacy"),
            C(2, "Chicken breast, oven-roasted, fat-free, sliced, with added solids", "SR Legacy"),
        };
        var mejor = SelectorMatchUsda.ElegirMejor(cands, "chicken breast");
        Assert.Equal(1, mejor!.FdcId);
    }

    [Fact]
    public void Lista_vacia_devuelve_null()
    {
        Assert.Null(SelectorMatchUsda.ElegirMejor(System.Array.Empty<CandidatoUsda>(), "x"));
    }
}
