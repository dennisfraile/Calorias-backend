using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Calorias.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calorias.Tests;

public class ServicioCorreccionPorcionesTests
{
    private static CaloriasDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<CaloriasDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options);

    private static RegistroComida ComidaConUnDetalle(out Guid detalleId)
    {
        var det = new DetalleComida
        {
            NombreAlimento = "Manzana", Cantidad = 100m, UnidadMedida = "g",
            Calorias = 52m, Proteinas = 0.3m, Carbohidratos = 14m, Grasas = 0.2m,
        };
        detalleId = det.Id;
        var reg = new RegistroComida
        {
            UsuarioId = "u1", FechaLocal = new DateOnly(2026, 6, 9), Tipo = TipoComida.Snack,
            Detalles = new List<DetalleComida> { det },
            CaloriasTotales = 52m, ProteinasTotales = 0.3m, CarbohidratosTotales = 14m, GrasasTotales = 0.2m,
        };
        return reg;
    }

    [Fact]
    public async Task Reescala_macros_proporcional_y_recalcula_totales()
    {
        using var db = NuevaDb();
        var reg = ComidaConUnDetalle(out var detId);
        db.Registros.Add(reg);
        await db.SaveChangesAsync();

        var svc = new ServicioCorreccionPorciones(db, new ServicioResumenDiario(db));
        var actualizado = await svc.CorregirAsync("u1", reg.Id,
            new[] { new CorreccionDetalle(detId, 200m) });

        Assert.NotNull(actualizado);
        var d = actualizado!.Detalles.Single();
        Assert.Equal(200m, d.Cantidad);
        Assert.Equal(104m, d.Calorias);          // 52 * (200/100)
        Assert.Equal(104m, actualizado.CaloriasTotales);
        var rollup = await db.ResumenesDiarios.SingleAsync();
        Assert.Equal(104m, rollup.CaloriasTotal);
    }

    [Fact]
    public async Task Cantidad_cero_elimina_el_detalle()
    {
        using var db = NuevaDb();
        var reg = ComidaConUnDetalle(out var detId);
        db.Registros.Add(reg);
        await db.SaveChangesAsync();

        var svc = new ServicioCorreccionPorciones(db, new ServicioResumenDiario(db));
        var actualizado = await svc.CorregirAsync("u1", reg.Id,
            new[] { new CorreccionDetalle(detId, 0m) });

        Assert.NotNull(actualizado);
        Assert.Empty(actualizado!.Detalles);
        Assert.Equal(0m, actualizado.CaloriasTotales);
        Assert.False(await db.ResumenesDiarios.AnyAsync());
    }

    [Fact]
    public async Task Registro_de_otro_usuario_devuelve_null()
    {
        using var db = NuevaDb();
        var reg = ComidaConUnDetalle(out var detId);
        db.Registros.Add(reg);
        await db.SaveChangesAsync();

        var svc = new ServicioCorreccionPorciones(db, new ServicioResumenDiario(db));
        var actualizado = await svc.CorregirAsync("otro", reg.Id,
            new[] { new CorreccionDetalle(detId, 200m) });

        Assert.Null(actualizado);
    }
}
