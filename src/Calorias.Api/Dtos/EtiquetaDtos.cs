namespace Calorias.Api.Dtos;

/// <summary>Respuesta de POST /api/comidas/leer-etiqueta (previsualización, no guarda).</summary>
public record EtiquetaNutricionalDto(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal? PorcionesPorEnvase,
    decimal CaloriasPorPorcion,
    decimal ProteinaPorPorcion,
    decimal CarbosPorPorcion,
    decimal GrasasPorPorcion);

/// <summary>Cuerpo de POST /api/comidas/guardar-etiqueta.</summary>
public record GuardarEtiquetaDto(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal CaloriasPorPorcion,
    decimal ProteinaPorPorcion,
    decimal CarbosPorPorcion,
    decimal GrasasPorPorcion,
    decimal Porciones,
    string Tipo,
    DateOnly FechaLocal);
