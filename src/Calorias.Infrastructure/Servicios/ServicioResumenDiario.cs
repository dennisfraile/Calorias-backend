using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Mantiene el rollup ResumenDiario con estrategia "recompute-the-day": recalcula el
/// total del día desde los registros reales. Idempotente; si el día queda sin comidas,
/// elimina la fila del rollup.
/// </summary>
public class ServicioResumenDiario(CaloriasDbContext db) : IServicioResumenDiario
{
    public async Task RecalcularDiaAsync(
        string usuarioId, DateOnly fechaLocal, CancellationToken ct = default)
    {
        var registros = await db.Registros
            .Where(r => r.UsuarioId == usuarioId && r.FechaLocal == fechaLocal)
            .ToListAsync(ct);

        var existente = await db.ResumenesDiarios
            .FirstOrDefaultAsync(x => x.UsuarioId == usuarioId && x.FechaLocal == fechaLocal, ct);

        if (registros.Count == 0)
        {
            if (existente is not null)
            {
                db.ResumenesDiarios.Remove(existente);
                await db.SaveChangesAsync(ct);
            }
            return;
        }

        var resumen = existente ?? new ResumenDiario { UsuarioId = usuarioId, FechaLocal = fechaLocal };
        resumen.CaloriasTotal  = registros.Sum(r => r.CaloriasTotales);
        resumen.ProteinasTotal = registros.Sum(r => r.ProteinasTotales);
        resumen.CarbosTotal    = registros.Sum(r => r.CarbohidratosTotales);
        resumen.GrasasTotal    = registros.Sum(r => r.GrasasTotales);
        resumen.NumComidas     = registros.Count;

        if (existente is null) db.ResumenesDiarios.Add(resumen);
        await db.SaveChangesAsync(ct);
    }
}
