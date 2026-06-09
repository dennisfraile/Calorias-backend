using Calorias.Application.Servicios;
using Xunit;

namespace Calorias.Tests;

public class EstimadorPorcionTests
{
    [Fact]
    public void Sin_porciones_usa_el_default()
    {
        Assert.Equal(150m, EstimadorPorcion.ElegirGramos(System.Array.Empty<decimal>()));
    }

    [Fact]
    public void Con_porciones_elige_la_primera_razonable()
    {
        // Ignora pesos no positivos y elige el primero válido.
        Assert.Equal(182m, EstimadorPorcion.ElegirGramos(new[] { 0m, 182m, 250m }));
    }

    [Fact]
    public void Descarta_pesos_absurdos_y_cae_al_default()
    {
        // > 1500 g se considera no representativo de una porción.
        Assert.Equal(150m, EstimadorPorcion.ElegirGramos(new[] { 5000m }));
    }
}
