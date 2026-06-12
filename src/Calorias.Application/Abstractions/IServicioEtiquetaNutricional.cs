namespace Calorias.Application.Abstractions;

/// <summary>Base en la que vienen expresados los macros de una etiqueta.</summary>
public enum BaseMedida { Porcion, Cien }

/// <summary>
/// Datos leídos de una etiqueta nutricional. Los macros se almacenan POR BASE
/// (<see cref="Base"/>) y se exponen POR PORCIÓN mediante las propiedades calculadas.
/// </summary>
public record EtiquetaNutricional(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal? PorcionesPorEnvase,
    BaseMedida Base,
    decimal CaloriasPorBase,
    decimal ProteinaPorBase,
    decimal CarbosPorBase,
    decimal GrasasPorBase)
{
    private bool UnidadEsMasa =>
        string.Equals(UnidadPorcion?.Trim(), "g", System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(UnidadPorcion?.Trim(), "ml", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Factor para pasar de "por base" a "por porción".</summary>
    private decimal FactorPorPorcion =>
        Base == BaseMedida.Cien && UnidadEsMasa && TamPorcion > 0m ? TamPorcion / 100m : 1m;

    public decimal CaloriasPorPorcion => CaloriasPorBase * FactorPorPorcion;
    public decimal ProteinaPorPorcion => ProteinaPorBase * FactorPorPorcion;
    public decimal CarbosPorPorcion => CarbosPorBase * FactorPorPorcion;
    public decimal GrasasPorPorcion => GrasasPorBase * FactorPorPorcion;
}

public interface IServicioEtiquetaNutricional
{
    /// <summary>Lee la etiqueta de una imagen. Devuelve null si no logra leer una etiqueta legible.</summary>
    System.Threading.Tasks.Task<EtiquetaNutricional?> LeerEtiquetaAsync(
        System.IO.Stream imagen, string mimeType, System.Threading.CancellationToken ct = default);
}
