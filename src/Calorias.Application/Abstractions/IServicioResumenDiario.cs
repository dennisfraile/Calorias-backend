namespace Calorias.Application.Abstractions;

public interface IServicioResumenDiario
{
    /// <summary>
    /// Recalcula (upsert/borra) el ResumenDiario de un usuario en un día concreto.
    /// Llama a SaveChanges internamente; si el llamador abrió una transacción, participa en ella.
    /// </summary>
    Task RecalcularDiaAsync(string usuarioId, DateOnly fechaLocal, CancellationToken ct = default);
}
