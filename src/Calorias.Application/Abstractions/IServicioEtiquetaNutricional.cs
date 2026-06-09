namespace Calorias.Application.Abstractions;

/// <summary>Datos leídos de una etiqueta nutricional (valores POR PORCIÓN).</summary>
public record EtiquetaNutricional(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal? PorcionesPorEnvase,
    decimal CaloriasPorPorcion,
    decimal ProteinaPorPorcion,
    decimal CarbosPorPorcion,
    decimal GrasasPorPorcion);

public interface IServicioEtiquetaNutricional
{
    /// <summary>Lee la etiqueta de una imagen. Devuelve null si no logra leer una etiqueta legible.</summary>
    Task<EtiquetaNutricional?> LeerEtiquetaAsync(
        Stream imagen, string mimeType, CancellationToken ct = default);
}
