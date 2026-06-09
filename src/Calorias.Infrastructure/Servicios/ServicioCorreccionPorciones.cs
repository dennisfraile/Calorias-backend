using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Infrastructure.Servicios;

public class ServicioCorreccionPorciones(CaloriasDbContext db, IServicioResumenDiario resumen)
    : IServicioCorreccionPorciones
{
    public async Task<RegistroComida?> CorregirAsync(
        string usuarioId, Guid registroId,
        IReadOnlyList<CorreccionDetalle> correcciones, CancellationToken ct = default)
    {
        var reg = await db.Registros
            .Include(r => r.Detalles)
            .FirstOrDefaultAsync(r => r.Id == registroId && r.UsuarioId == usuarioId, ct);
        if (reg is null) return null;

        var porId = correcciones.ToDictionary(c => c.DetalleId, c => c.CantidadG);

        foreach (var d in reg.Detalles.ToList())
        {
            if (!porId.TryGetValue(d.Id, out var nuevaG)) continue;

            if (nuevaG <= 0m)
            {
                reg.Detalles.Remove(d);
                db.Remove(d);
                continue;
            }

            if (d.Cantidad > 0m)
            {
                var f = nuevaG / d.Cantidad;
                d.Calorias      = Math.Round(d.Calorias * f, 2);
                d.Proteinas     = Math.Round(d.Proteinas * f, 2);
                d.Carbohidratos = Math.Round(d.Carbohidratos * f, 2);
                d.Grasas        = Math.Round(d.Grasas * f, 2);
            }
            d.Cantidad = nuevaG;
        }

        reg.CaloriasTotales      = reg.Detalles.Sum(x => x.Calorias);
        reg.ProteinasTotales     = reg.Detalles.Sum(x => x.Proteinas);
        reg.CarbohidratosTotales = reg.Detalles.Sum(x => x.Carbohidratos);
        reg.GrasasTotales        = reg.Detalles.Sum(x => x.Grasas);

        // Un registro sin detalles ya no es una comida: lo eliminamos para que el
        // rollup del día se recalcule (o se borre) correctamente.
        if (reg.Detalles.Count == 0)
            db.Remove(reg);

        await db.SaveChangesAsync(ct);
        await resumen.RecalcularDiaAsync(usuarioId, reg.FechaLocal, ct);
        return reg;
    }
}
