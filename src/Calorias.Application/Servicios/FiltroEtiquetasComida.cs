using Calorias.Application.Abstractions;

namespace Calorias.Application.Servicios;

/// <summary>
/// Quita de las etiquetas de Vision los términos genéricos/no-comida (denylist) y
/// deduplica por nombre normalizado (singular/plural), conservando la mayor confianza.
/// Puro: sin estado ni dependencias. Mantiene el orden de aparición.
/// </summary>
public static class FiltroEtiquetasComida
{
    private static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "food", "dish", "cuisine", "ingredient", "recipe", "produce", "meal", "tableware",
        "dishware", "plate", "bowl", "cutlery", "fork", "spoon", "knife", "drink", "drinkware",
        "breakfast", "lunch", "dinner", "brunch", "supper", "fast food", "junk food",
        "comfort food", "finger food", "whole food", "natural foods", "vegetarian food",
        "superfood", "staple food", "side dish", "garnish", "snack", "tablecloth",
        "dessert", "baked goods", "cooking", "frozen food", "processed food", "leaf vegetable",
    };

    public static IReadOnlyList<EtiquetaDetectada> Filtrar(IReadOnlyList<EtiquetaDetectada> etiquetas)
    {
        var mejorPorClave = new Dictionary<string, EtiquetaDetectada>();
        var orden = new List<string>();

        foreach (var e in etiquetas)
        {
            var nombre = e.Descripcion.Trim();
            if (nombre.Length == 0 || Denylist.Contains(nombre)) continue;

            var clave = Normalizar(nombre);
            if (mejorPorClave.TryGetValue(clave, out var prev))
            {
                if (e.Confianza > prev.Confianza) mejorPorClave[clave] = e;
            }
            else
            {
                mejorPorClave[clave] = e;
                orden.Add(clave);
            }
        }

        return orden.Select(c => mejorPorClave[c]).ToList();
    }

    /// <summary>Minúsculas + quita una 's' final simple para unir singular/plural.</summary>
    private static string Normalizar(string s)
    {
        var x = s.Trim().ToLowerInvariant();
        return x.Length > 3 && x.EndsWith('s') ? x[..^1] : x;
    }
}
