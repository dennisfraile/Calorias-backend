using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Xunit;

namespace Calorias.Tests;

public class TraductorAlimentosTests
{
    private sealed class FakeTraduccion(string? respuesta) : IServicioTraduccion
    {
        public int Llamadas { get; private set; }
        public Task<string?> TraducirAlEspanolAsync(string textoIngles, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(respuesta);
        }
    }

    [Fact]
    public async Task Termino_en_diccionario_no_llama_al_servicio()
    {
        var fake = new FakeTraduccion("NO-DEBERIA-USARSE");
        var t = new TraductorAlimentos(fake);
        var r = await t.TraducirNombreAsync("Apple");
        Assert.Equal("Manzana", r);
        Assert.Equal(0, fake.Llamadas);
    }

    [Fact]
    public async Task Termino_ausente_usa_el_servicio_y_cachea()
    {
        var fake = new FakeTraduccion("Ensalada de quinoa");
        var t = new TraductorAlimentos(fake);
        var r1 = await t.TraducirNombreAsync("Quinoa salad, prepared");
        var r2 = await t.TraducirNombreAsync("Quinoa salad, prepared");
        Assert.Equal("Ensalada de quinoa", r1);
        Assert.Equal("Ensalada de quinoa", r2);
        Assert.Equal(1, fake.Llamadas); // la 2ª vez sale de la caché
    }

    [Fact]
    public async Task Si_el_servicio_no_traduce_cae_al_ingles()
    {
        var fake = new FakeTraduccion(null);
        var t = new TraductorAlimentos(fake);
        var r = await t.TraducirNombreAsync("Exotic unknown dish");
        Assert.Equal("Exotic unknown dish", r);
    }
}
