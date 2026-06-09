using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Xunit;

namespace Calorias.Tests;

public class FiltroEtiquetasComidaTests
{
    private static EtiquetaDetectada E(string d, float c = 0.9f) => new(d, c);

    [Fact]
    public void Descarta_etiquetas_genericas_no_comida()
    {
        var entrada = new[] { E("Food"), E("Dish"), E("Tableware"), E("Apple"), E("White rice") };
        var salida = FiltroEtiquetasComida.Filtrar(entrada);
        Assert.Equal(new[] { "Apple", "White rice" }, salida.Select(e => e.Descripcion).ToArray());
    }

    [Fact]
    public void Es_insensible_a_mayusculas()
    {
        var salida = FiltroEtiquetasComida.Filtrar(new[] { E("FOOD"), E("cuisine"), E("Banana") });
        Assert.Single(salida);
        Assert.Equal("Banana", salida[0].Descripcion);
    }

    [Fact]
    public void Deduplica_singular_plural_conservando_mayor_confianza()
    {
        var salida = FiltroEtiquetasComida.Filtrar(new[] { E("Apple", 0.7f), E("Apples", 0.95f) });
        Assert.Single(salida);
        Assert.Equal(0.95f, salida[0].Confianza);
    }

    [Fact]
    public void Si_todo_es_generico_devuelve_vacio()
    {
        var salida = FiltroEtiquetasComida.Filtrar(new[] { E("Food"), E("Meal"), E("Plate") });
        Assert.Empty(salida);
    }
}
