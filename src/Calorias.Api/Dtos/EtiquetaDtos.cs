namespace Calorias.Api.Dtos;

/// <summary>Respuesta de POST /api/comidas/leer-etiqueta (previsualización, no guarda).
/// Macros POR BASE; 'Base' = "porcion" | "cien".</summary>
public record EtiquetaNutricionalDto(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal? PorcionesPorEnvase,
    string Base,
    decimal CaloriasPorBase,
    decimal ProteinaPorBase,
    decimal CarbosPorBase,
    decimal GrasasPorBase);

/// <summary>Cuerpo de POST /api/comidas/guardar-etiqueta. Macros POR BASE.</summary>
public record GuardarEtiquetaDto(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    string Base,
    decimal CaloriasPorBase,
    decimal ProteinaPorBase,
    decimal CarbosPorBase,
    decimal GrasasPorBase,
    decimal Porciones,
    string Tipo,
    DateOnly FechaLocal);
