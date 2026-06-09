namespace Calorias.Application.Servicios;

/// <summary>Candidato de búsqueda de USDA FoodData Central (datos mínimos para puntuar).</summary>
public record CandidatoUsda(int FdcId, string Description, string DataType);

/// <summary>
/// Elige el mejor match USDA para una consulta. Puro y testeable (sin red).
/// Puntúa por: solape de tokens query↔description (peso principal), bonus a Foundation,
/// preferencia por descripciones cortas/crudas y penalización por exceso de calificadores.
/// </summary>
public static class SelectorMatchUsda
{
    public static CandidatoUsda? ElegirMejor(IReadOnlyList<CandidatoUsda> candidatos, string query)
    {
        if (candidatos.Count == 0) return null;

        var qTokens = Tokenizar(query);
        CandidatoUsda? mejor = null;
        double mejorScore = double.NegativeInfinity;

        foreach (var c in candidatos)
        {
            var score = Puntuar(c, qTokens);
            if (score > mejorScore)
            {
                mejorScore = score;
                mejor = c;
            }
        }
        return mejor;
    }

    private static double Puntuar(CandidatoUsda c, HashSet<string> qTokens)
    {
        var dTokens = Tokenizar(c.Description);
        int solape = dTokens.Count(t => qTokens.Contains(t));

        double score = solape * 10.0;                                   // (a) solape de tokens
        if (c.DataType.Equals("Foundation", StringComparison.OrdinalIgnoreCase))
            score += 2.0;                                               // (b) bonus Foundation
        score -= dTokens.Count * 0.5;                                   // (c) preferir corto/crudo
        score -= c.Description.Count(ch => ch == ',') * 0.5;            // (d) penalizar calificadores
        if (dTokens.Contains("raw")) score += 1.0;                      // ligeramente a favor de "raw"
        return score;
    }

    private static HashSet<string> Tokenizar(string s) =>
        s.ToLowerInvariant()
         .Split(new[] { ' ', ',', '.', '(', ')', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
         .ToHashSet();
}
