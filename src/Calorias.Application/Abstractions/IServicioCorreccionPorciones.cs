using Calorias.Domain.Entities;

namespace Calorias.Application.Abstractions;

/// <summary>Una corrección de porción: nuevos gramos para un detalle (0 = eliminar).</summary>
public record CorreccionDetalle(Guid DetalleId, decimal CantidadG);

public interface IServicioCorreccionPorciones
{
    /// <summary>
    /// Reescala (proporcional) o elimina detalles de un registro propio del usuario,
    /// recalcula totales y el rollup diario. Devuelve el registro actualizado, o null
    /// si no existe o no pertenece al usuario.
    /// </summary>
    Task<RegistroComida?> CorregirAsync(
        string usuarioId, Guid registroId,
        IReadOnlyList<CorreccionDetalle> correcciones, CancellationToken ct = default);
}
