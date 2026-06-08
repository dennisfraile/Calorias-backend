using Calorias.Application.Resultados;
using Calorias.Domain.Entities;

namespace Calorias.Application.Abstractions;

public interface IServicioResumenPeriodos
{
    /// <summary>Resumen comparativo (actual vs anterior) del período, con meta del perfil.</summary>
    Task<ResumenPeriodos> ObtenerAsync(
        string usuarioId, Periodo periodo, DateOnly ancla, CancellationToken ct = default);
}
