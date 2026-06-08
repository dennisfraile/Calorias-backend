using Calorias.Domain.Entities;
using Calorias.Domain.Servicios;
using Xunit;

namespace Calorias.Tests;

public class RangosPeriodoTests
{
    [Fact]
    public void Diario_actual_y_anterior_son_un_dia()
    {
        var r = RangosPeriodo.Calcular(Periodo.Diario, new DateOnly(2026, 6, 8));
        Assert.Equal(new DateOnly(2026, 6, 8), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 6, 8), r.ActualFin);
        Assert.Equal(new DateOnly(2026, 6, 7), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2026, 6, 7), r.AnteriorFin);
    }

    [Fact]
    public void Semanal_empieza_en_lunes_y_cubre_siete_dias()
    {
        var ancla = new DateOnly(2026, 6, 10);
        var r = RangosPeriodo.Calcular(Periodo.Semanal, ancla);
        Assert.Equal(DayOfWeek.Monday, r.ActualInicio.DayOfWeek);
        Assert.Equal(r.ActualInicio.AddDays(6), r.ActualFin);
        Assert.True(r.ActualInicio <= ancla && ancla <= r.ActualFin);
        Assert.Equal(r.ActualInicio.AddDays(-7), r.AnteriorInicio);
        Assert.Equal(r.ActualInicio.AddDays(-1), r.AnteriorFin);
    }

    [Fact]
    public void Mensual_cubre_el_mes_y_el_anterior()
    {
        var r = RangosPeriodo.Calcular(Periodo.Mensual, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 6, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 6, 30), r.ActualFin);
        Assert.Equal(new DateOnly(2026, 5, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2026, 5, 31), r.AnteriorFin);
    }

    [Fact]
    public void Mensual_en_enero_el_anterior_es_diciembre_previo()
    {
        var r = RangosPeriodo.Calcular(Periodo.Mensual, new DateOnly(2026, 1, 10));
        Assert.Equal(new DateOnly(2026, 1, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 1, 31), r.ActualFin);
        Assert.Equal(new DateOnly(2025, 12, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2025, 12, 31), r.AnteriorFin);
    }

    [Fact]
    public void Trimestral_q2_y_q1()
    {
        var r = RangosPeriodo.Calcular(Periodo.Trimestral, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 4, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 6, 30), r.ActualFin);
        Assert.Equal(new DateOnly(2026, 1, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2026, 3, 31), r.AnteriorFin);
    }

    [Fact]
    public void Semestral_h1_y_h2_previo()
    {
        var r = RangosPeriodo.Calcular(Periodo.Semestral, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 1, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 6, 30), r.ActualFin);
        Assert.Equal(new DateOnly(2025, 7, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2025, 12, 31), r.AnteriorFin);
    }

    [Fact]
    public void Anual_actual_y_anterior()
    {
        var r = RangosPeriodo.Calcular(Periodo.Anual, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 1, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 12, 31), r.ActualFin);
        Assert.Equal(new DateOnly(2025, 1, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2025, 12, 31), r.AnteriorFin);
    }
}
