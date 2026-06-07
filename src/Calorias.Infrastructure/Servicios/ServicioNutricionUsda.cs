using System.Text.Json;
using Calorias.Application.Abstractions;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Nutrición vía USDA FoodData Central (https://fdc.nal.usda.gov/), API pública gratuita.
/// A diferencia de Nutritionix (lenguaje natural), USDA es por búsqueda: por cada alimento
/// detectado por Vision se busca el primer match y se leen sus macros (valores por 100 g).
/// La API key viaja en el header X-Api-Key (configurada en el HttpClient tipado).
/// </summary>
public class ServicioNutricionUsda(HttpClient http) : IServicioNutricion
{
    // IDs de nutrientes en FoodData Central.
    private const int EnergiaKcal = 1008;             // Energy (KCAL)
    private const int EnergiaAtwaterGeneral = 2047;   // Energy (Atwater General Factors)
    private const int EnergiaAtwaterEspecifico = 2048; // Energy (Atwater Specific Factors)
    private const int Proteina = 1003;
    private const int Carbohidratos = 1005;           // Carbohydrate, by difference
    private const int Grasa = 1004;                   // Total lipid (fat)

    public async Task<ResultadoNutricional> ObtenerMacrosAsync(
        IReadOnlyList<string> alimentos, CancellationToken ct = default)
    {
        var resultados = new List<AlimentoNutricional>();
        var crudos = new List<string>();

        foreach (var alimento in alimentos)
        {
            // Solo datos de referencia estándar (valores por 100 g) y el primer match.
            var url = $"v1/foods/search?query={Uri.EscapeDataString(alimento)}"
                    + "&dataType=Foundation,SR%20Legacy&pageSize=1";

            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) continue;

            var json = await resp.Content.ReadAsStringAsync(ct);
            crudos.Add(json);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("foods", out var foods)
                || foods.GetArrayLength() == 0) continue;

            var food = foods[0];
            var nombre = food.TryGetProperty("description", out var desc)
                ? desc.GetString() ?? alimento
                : alimento;

            decimal Nutriente(params int[] ids)
            {
                if (!food.TryGetProperty("foodNutrients", out var ns)) return 0m;
                foreach (var n in ns.EnumerateArray())
                {
                    if (n.TryGetProperty("nutrientId", out var idEl)
                        && ids.Contains(idEl.GetInt32())
                        && n.TryGetProperty("value", out var v)
                        && v.TryGetDecimal(out var dec))
                        return dec;
                }
                return 0m;
            }

            resultados.Add(new AlimentoNutricional(
                Nombre:        nombre,
                Calorias:      Nutriente(EnergiaKcal, EnergiaAtwaterGeneral, EnergiaAtwaterEspecifico),
                Proteinas:     Nutriente(Proteina),
                Carbohidratos: Nutriente(Carbohidratos),
                Grasas:        Nutriente(Grasa),
                Cantidad:      100m,
                Unidad:        "g"));
        }

        // Snapshot crudo (jsonb) para auditoría: array con las respuestas de cada búsqueda.
        var jsonCrudo = "[" + string.Join(",", crudos) + "]";
        return new ResultadoNutricional(resultados, jsonCrudo);
    }
}
