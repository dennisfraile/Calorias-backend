namespace Calorias.Application.Abstractions;

public interface IServicioResumenDiario
{
    /// <summary>Recalcula (upsert/borra) el ResumenDiario de un usuario en un día concreto.</summary>
    Task RecalcularDiaAsync(string usuarioId, DateOnly fechaLocal, CancellationToken ct = default);
}
