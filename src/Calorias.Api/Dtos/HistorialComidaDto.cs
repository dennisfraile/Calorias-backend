namespace Calorias.Api.Dtos;

/// <summary>
/// Fila del historial para el frontend (campos que consume el AppSpec de Mythik:
/// id, nombre, hora, calorias, proteinas, carbohidratos, grasas).
/// </summary>
public record HistorialComidaDto(
    Guid Id,
    string Nombre,
    string Fecha,
    string Hora,
    decimal Calorias,
    decimal Proteinas,
    decimal Carbohidratos,
    decimal Grasas);
