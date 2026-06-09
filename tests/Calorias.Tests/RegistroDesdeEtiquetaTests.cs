using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Calorias.Domain.Entities;
using Xunit;

namespace Calorias.Tests;

public class RegistroDesdeEtiquetaTests
{
    private static EtiquetaNutricional Refresco() => new(
        NombreProducto: "Kolashanpan",
        TamPorcion: 250m, UnidadPorcion: "mL", PorcionesPorEnvase: 1.4m,
        CaloriasPorPorcion: 84m, ProteinaPorPorcion: 0m, CarbosPorPorcion: 21m, GrasasPorPorcion: 0m);

    [Fact]
    public void Escala_macros_y_cantidad_por_porciones()
    {
        var reg = RegistroDesdeEtiqueta.Construir(
            Refresco(), porciones: 1.4m, usuarioId: "u1",
            tipo: TipoComida.Snack, fechaLocal: new System.DateOnly(2026, 6, 9));

        var d = Assert.Single(reg.Detalles);
        Assert.Equal("Kolashanpan", d.NombreAlimento);
        Assert.Equal(350m, d.Cantidad);            // 250 * 1.4
        Assert.Equal("mL", d.UnidadMedida);
        Assert.Equal(117.6m, d.Calorias);          // 84 * 1.4
        Assert.Equal(29.4m, d.Carbohidratos);      // 21 * 1.4
        Assert.Equal(0m, d.Proteinas);
        Assert.Equal(1d, d.ConfianzaDeteccion);
    }

    [Fact]
    public void Totales_del_registro_igualan_el_unico_detalle()
    {
        var reg = RegistroDesdeEtiqueta.Construir(
            Refresco(), 2m, "u1", TipoComida.Desayuno, new System.DateOnly(2026, 6, 9));

        Assert.Equal("u1", reg.UsuarioId);
        Assert.Equal(TipoComida.Desayuno, reg.Tipo);
        Assert.Equal(168m, reg.CaloriasTotales);   // 84 * 2
        Assert.Equal(42m, reg.CarbohidratosTotales);
        Assert.Equal(reg.Detalles.First().Calorias, reg.CaloriasTotales);
    }

    [Fact]
    public void Nombre_vacio_cae_a_Producto()
    {
        var etq = Refresco() with { NombreProducto = null };
        var reg = RegistroDesdeEtiqueta.Construir(etq, 1m, "u1", TipoComida.Cena, new System.DateOnly(2026, 6, 9));
        Assert.Equal("Producto", reg.Detalles.First().NombreAlimento);
    }
}
