using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Calorias.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calorias.Tests;

public class ServicioResumenPeriodosTests
{
    private static CaloriasDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<CaloriasDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options);

    private static ResumenDiario Dia(string u, DateOnly d, decimal kcal, decimal prot, decimal carb, decimal gr) =>
        new() { UsuarioId = u, FechaLocal = d, CaloriasTotal = kcal, ProteinasTotal = prot, CarbosTotal = carb, GrasasTotal = gr, NumComidas = 1 };

    [Fact]
    public async Task Semanal_promedia_los_dias_con_registro_y_arma_siete_buckets()
    {
        using var db = NuevaDb();
        db.ResumenesDiarios.AddRange(
            Dia("u1", new DateOnly(2026, 6, 8), 2000, 100, 200, 60),
            Dia("u1", new DateOnly(2026, 6, 9), 1000, 50, 100, 30),
            Dia("u1", new DateOnly(2026, 6, 10), 1500, 75, 150, 45));
        await db.SaveChangesAsync();

        var r = await new ServicioResumenPeriodos(db)
            .ObtenerAsync("u1", Periodo.Semanal, new DateOnly(2026, 6, 10));

        Assert.Equal(1500, r.Actual.PromedioKcalDia);
        Assert.Equal(7, r.Actual.Buckets.Count);
        Assert.Equal(2000, r.Actual.Buckets[0].Kcal);
        Assert.Equal(1000, r.Actual.Buckets[1].Kcal);
        Assert.Equal(0, r.Actual.Buckets[3].Kcal);
        Assert.Equal("L", r.Actual.Buckets[0].Etiqueta);
        Assert.Equal("D", r.Actual.Buckets[6].Etiqueta);
    }

    [Fact]
    public async Task Sin_datos_el_promedio_es_cero_y_no_revienta()
    {
        using var db = NuevaDb();
        var r = await new ServicioResumenPeriodos(db)
            .ObtenerAsync("u1", Periodo.Semanal, new DateOnly(2026, 6, 10));
        Assert.Equal(0, r.Actual.PromedioKcalDia);
        Assert.Equal(7, r.Actual.Buckets.Count);
        Assert.Null(r.Meta);
        Assert.Null(r.CambioPct);
    }

    [Fact]
    public async Task Diario_arma_cuatro_buckets_por_tipo_de_comida()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 8);
        db.Registros.AddRange(
            new RegistroComida { UsuarioId = "u1", FechaLocal = dia, Tipo = TipoComida.Desayuno, CaloriasTotales = 300, ProteinasTotales = 10, CarbohidratosTotales = 40, GrasasTotales = 8 },
            new RegistroComida { UsuarioId = "u1", FechaLocal = dia, Tipo = TipoComida.Almuerzo, CaloriasTotales = 700, ProteinasTotales = 30, CarbohidratosTotales = 80, GrasasTotales = 20 });
        db.ResumenesDiarios.Add(Dia("u1", dia, 1000, 40, 120, 28));
        await db.SaveChangesAsync();

        var r = await new ServicioResumenPeriodos(db).ObtenerAsync("u1", Periodo.Diario, dia);

        Assert.Equal(4, r.Actual.Buckets.Count);
        Assert.Equal("Desayuno", r.Actual.Buckets[0].Etiqueta);
        Assert.Equal(300, r.Actual.Buckets[0].Kcal);
        Assert.Equal("Almuerzo", r.Actual.Buckets[1].Etiqueta);
        Assert.Equal(700, r.Actual.Buckets[1].Kcal);
        Assert.Equal(0, r.Actual.Buckets[2].Kcal);
        Assert.Equal(1000, r.Actual.PromedioKcalDia);
    }

    [Fact]
    public async Task CambioPct_compara_promedios_actual_vs_anterior()
    {
        using var db = NuevaDb();
        db.ResumenesDiarios.AddRange(
            Dia("u1", new DateOnly(2026, 6, 5), 1000, 0, 0, 0),
            Dia("u1", new DateOnly(2026, 5, 5), 2000, 0, 0, 0));
        await db.SaveChangesAsync();

        var r = await new ServicioResumenPeriodos(db)
            .ObtenerAsync("u1", Periodo.Mensual, new DateOnly(2026, 6, 15));

        Assert.Equal(1000, r.Actual.PromedioKcalDia);
        Assert.Equal(2000, r.Anterior.PromedioKcalDia);
        Assert.Equal(-50.0, r.CambioPct);
    }
}
