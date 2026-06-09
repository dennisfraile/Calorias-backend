namespace Calorias.Api.Dtos;

/// <summary>Cuerpo de PUT /api/comidas/{id}/porciones.</summary>
public record CorreccionPorcionesDto(IReadOnlyList<CorreccionDetalleDto> Detalles);

public record CorreccionDetalleDto(Guid DetalleId, decimal CantidadG);
