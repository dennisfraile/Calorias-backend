using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Calorias.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calorias.Tests;

public class ServicioResumenDiarioTests
{
    private static CaloriasDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<CaloriasDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options);

    private static RegistroComida Comida(string usuario, DateOnly dia, TipoComida tipo,
        decimal kcal, decimal prot, decimal carbs, decimal grasas) => new()
    {
        UsuarioId = usuario,
        FechaLocal = dia,
        Tipo = tipo,
        CaloriasTotales = kcal,
        ProteinasTotales = prot,
        CarbohidratosTotales = carbs,
        GrasasTotales = grasas,
    };

    [Fact]
    public async Task Recalcular_suma_los_registros_del_dia()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 7);
        db.Registros.AddRange(
            Comida("u1", dia, TipoComida.Desayuno, 300, 10, 40, 8),
            Comida("u1", dia, TipoComida.Almuerzo, 700, 30, 80, 20));
        await db.SaveChangesAsync();

        await new ServicioResumenDiario(db).RecalcularDiaAsync("u1", dia);

        var r = await db.ResumenesDiarios.SingleAsync();
        Assert.Equal(1000m, r.CaloriasTotal);
        Assert.Equal(40m, r.ProteinasTotal);
        Assert.Equal(120m, r.CarbosTotal);
        Assert.Equal(28m, r.GrasasTotal);
        Assert.Equal(2, r.NumComidas);
    }

    [Fact]
    public async Task Recalcular_es_idempotente_y_actualiza_el_existente()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 7);
        db.Registros.Add(Comida("u1", dia, TipoComida.Cena, 500, 20, 50, 15));
        await db.SaveChangesAsync();
        var svc = new ServicioResumenDiario(db);

        await svc.RecalcularDiaAsync("u1", dia);
        await svc.RecalcularDiaAsync("u1", dia); // segunda vez: no duplica

        var r = await db.ResumenesDiarios.SingleAsync();
        Assert.Equal(500m, r.CaloriasTotal);
        Assert.Equal(1, r.NumComidas);
    }

    [Fact]
    public async Task Recalcular_borra_el_resumen_si_no_quedan_registros()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 7);
        db.ResumenesDiarios.Add(new ResumenDiario
        {
            UsuarioId = "u1", FechaLocal = dia, CaloriasTotal = 999, NumComidas = 1
        });
        await db.SaveChangesAsync();

        await new ServicioResumenDiario(db).RecalcularDiaAsync("u1", dia);

        Assert.False(await db.ResumenesDiarios.AnyAsync());
    }

    [Fact]
    public async Task Recalcular_solo_cuenta_los_registros_del_usuario_indicado()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 7);
        db.Registros.AddRange(
            Comida("u1", dia, TipoComida.Desayuno, 300, 10, 40, 8),
            Comida("u2", dia, TipoComida.Almuerzo, 999, 99, 99, 99));
        await db.SaveChangesAsync();

        await new ServicioResumenDiario(db).RecalcularDiaAsync("u1", dia);

        var r = await db.ResumenesDiarios.SingleAsync();
        Assert.Equal("u1", r.UsuarioId);
        Assert.Equal(300m, r.CaloriasTotal);
        Assert.Equal(1, r.NumComidas);
    }
}
