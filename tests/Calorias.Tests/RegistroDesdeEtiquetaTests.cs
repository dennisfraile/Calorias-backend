using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Calorias.Domain.Entities;
using Xunit;

namespace Calorias.Tests;

public class RegistroDesdeEtiquetaTests
{
    // Etiqueta POR PORCIÓN (base = Porcion): valores tal cual.
    private static EtiquetaNutricional Refresco() => new(
        NombreProducto: "Kolashanpan",
        TamPorcion: 250m, UnidadPorcion: "mL", PorcionesPorEnvase: 1.4m,
        Base: BaseMedida.Porcion,
        CaloriasPorBase: 84m, ProteinaPorBase: 0m, CarbosPorBase: 21m, GrasasPorBase: 0m);

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

    [Fact]
    public void Base_cien_con_unidad_masa_convierte_a_porcion()
    {
        // Galleta: 500 kcal por 100 g, porción de 30 g => 150 kcal por porción.
        var etq = new EtiquetaNutricional(
            NombreProducto: "Galletas", TamPorcion: 30m, UnidadPorcion: "g", PorcionesPorEnvase: 8m,
            Base: BaseMedida.Cien,
            CaloriasPorBase: 500m, ProteinaPorBase: 10m, CarbosPorBase: 60m, GrasasPorBase: 25m);

        var reg = RegistroDesdeEtiqueta.Construir(etq, porciones: 2m, usuarioId: "u1",
            tipo: TipoComida.Snack, fechaLocal: new System.DateOnly(2026, 6, 12));

        var d = Assert.Single(reg.Detalles);
        Assert.Equal(60m, d.Cantidad);             // 30 g * 2 porciones
        Assert.Equal(300m, d.Calorias);            // (500 * 30/100) * 2 = 150 * 2
        Assert.Equal(6m, d.Proteinas);             // (10 * 0.3) * 2
        Assert.Equal(36m, d.Carbohidratos);        // (60 * 0.3) * 2
        Assert.Equal(15m, d.Grasas);               // (25 * 0.3) * 2
    }

    [Fact]
    public void Base_cien_con_unidad_no_masa_se_trata_como_porcion()
    {
        // Unidad "unidad" no permite la conversión por 100 => factor 1.
        var etq = new EtiquetaNutricional(
            NombreProducto: "Barra", TamPorcion: 1m, UnidadPorcion: "unidad", PorcionesPorEnvase: 6m,
            Base: BaseMedida.Cien,
            CaloriasPorBase: 90m, ProteinaPorBase: 2m, CarbosPorBase: 12m, GrasasPorBase: 3m);

        var reg = RegistroDesdeEtiqueta.Construir(etq, 1m, "u1", TipoComida.Snack, new System.DateOnly(2026, 6, 12));

        var d = Assert.Single(reg.Detalles);
        Assert.Equal(90m, d.Calorias);             // factor 1 (no es masa)
        Assert.Equal(2m, d.Proteinas);
    }
}
