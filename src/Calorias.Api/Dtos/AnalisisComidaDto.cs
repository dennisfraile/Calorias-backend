namespace Calorias.Api.Dtos;

/// <summary>
/// Respuesta de POST /api/comidas/analizar: el registro recién creado, aplanado
/// para el frontend. Evita devolver la entidad EF (cuya navegación
/// DetalleComida → RegistroComida provoca un ciclo al serializar a JSON).
/// </summary>
public record AnalisisComidaDto(
    Guid Id,
    string Tipo,
    string FechaLocal,
    string Fecha,
    decimal CaloriasTotales,
    decimal ProteinasTotales,
    decimal CarbohidratosTotales,
    decimal GrasasTotales,
    IReadOnlyList<DetalleComidaDto> Detalles);

public record DetalleComidaDto(
    string Nombre,
    decimal Calorias,
    decimal Proteinas,
    decimal Carbohidratos,
    decimal Grasas,
    decimal Cantidad,
    string Unidad);
