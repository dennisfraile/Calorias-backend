namespace Calorias.Application.Servicios;

/// <summary>
/// Elige los gramos de una porción a partir de los gramWeight de USDA (foodPortions).
/// Toma el primer peso "razonable" (0 &lt; g ≤ 1500); si no hay, usa un default sensato.
/// Puro y testeable.
/// </summary>
public static class EstimadorPorcion
{
    public const decimal GramosDefault = 150m;
    private const decimal MaxRazonable = 1500m;

    public static decimal ElegirGramos(IReadOnlyList<decimal> gramWeights)
    {
        foreach (var g in gramWeights)
            if (g > 0m && g <= MaxRazonable) return g;
        return GramosDefault;
    }
}
