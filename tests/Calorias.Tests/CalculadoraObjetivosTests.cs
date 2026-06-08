using Calorias.Domain.Entities;
using Calorias.Domain.Servicios;
using Xunit;

namespace Calorias.Tests;

public class CalculadoraObjetivosTests
{
    // Hombre, mantener, sedentario: TMB = 10*80 + 6.25*180 - 5*30 + 5 = 1780;
    // TDEE = 1780 * 1.2 = 2136; sin ajuste; por encima del piso => meta 2136.
    [Fact]
    public void Hombre_mantener_sedentario_sin_override()
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Masculino, edad: 30, alturaCm: 180, pesoKg: 80m,
            NivelActividad.Sedentario, Objetivo.Mantener, ritmoKgSemana: 0m,
            metaCaloriasOverride: null, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.Equal(1780, r.Tmb);
        Assert.Equal(2136, r.Tdee);
        Assert.Equal(2136, r.MetaCalorias);
        Assert.False(r.MetaTopada);
        // Macros: prot 30%*2136/4=160.2->160; carbs 40%*2136/4=213.6->214; grasas 30%*2136/9=71.2->71
        Assert.Equal(160, r.ProteinaG);
        Assert.Equal(214, r.CarbosG);
        Assert.Equal(71, r.GrasasG);
    }

    // Mujer, perder, moderado: TMB = 10*60 + 6.25*165 - 5*28 - 161 = 1330.25;
    // TDEE = 1330.25*1.55 = 2061.8875; ajuste = 0.5*7700/7 = 550; perder => 1511.8875;
    // piso = max(1330.25, 1200) = 1330.25 (no topa); meta = round(1511.8875) = 1512.
    [Fact]
    public void Mujer_perder_moderado_sin_override()
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Femenino, edad: 28, alturaCm: 165, pesoKg: 60m,
            NivelActividad.Moderado, Objetivo.Perder, ritmoKgSemana: 0.5m,
            metaCaloriasOverride: null, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.Equal(1330, r.Tmb);
        Assert.Equal(2062, r.Tdee);
        Assert.Equal(1512, r.MetaCalorias);
        Assert.False(r.MetaTopada);
    }

    // Mujer agresiva: TMB = 550+1000-125-161 = 1264; TDEE = 1264*1.2 = 1516.8;
    // ajuste = 1.0*7700/7 = 1100; perder => 416.8; piso = max(1264,1200)=1264;
    // 416.8 < 1264 => topa, meta = 1264.
    [Fact]
    public void Deficit_agresivo_se_topa_en_el_piso_de_seguridad()
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Femenino, edad: 25, alturaCm: 160, pesoKg: 55m,
            NivelActividad.Sedentario, Objetivo.Perder, ritmoKgSemana: 1.0m,
            metaCaloriasOverride: null, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.True(r.MetaTopada);
        Assert.Equal(1264, r.MetaCalorias);
    }

    // Override manda: cálculo daría 2136, pero override 1900 => meta 1900 y no topada.
    [Fact]
    public void Override_manda_sobre_el_calculo()
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Masculino, edad: 30, alturaCm: 180, pesoKg: 80m,
            NivelActividad.Sedentario, Objetivo.Mantener, ritmoKgSemana: 0m,
            metaCaloriasOverride: 1900, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.Equal(1900, r.MetaCalorias);
        Assert.False(r.MetaTopada);
        // Macros desde 1900: prot 30%*1900/4=142.5->143 (AwayFromZero); carbs 190; grasas 63.33->63
        Assert.Equal(143, r.ProteinaG);
        Assert.Equal(190, r.CarbosG);
        Assert.Equal(63, r.GrasasG);
    }

    [Theory]
    [InlineData(NivelActividad.Sedentario, 2136)]
    [InlineData(NivelActividad.Ligero, 2448)]    // 1780*1.375 = 2447.5 -> 2448
    [InlineData(NivelActividad.Moderado, 2759)]  // 1780*1.55  = 2759
    [InlineData(NivelActividad.Activo, 3071)]    // 1780*1.725 = 3070.5 -> 3071
    [InlineData(NivelActividad.MuyActivo, 3382)] // 1780*1.9   = 3382
    public void Factor_de_actividad_se_aplica(NivelActividad nivel, int metaEsperada)
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Masculino, edad: 30, alturaCm: 180, pesoKg: 80m,
            nivel, Objetivo.Mantener, ritmoKgSemana: 0m,
            metaCaloriasOverride: null, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.Equal(metaEsperada, r.MetaCalorias);
    }
}
