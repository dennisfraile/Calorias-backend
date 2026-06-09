using System.Text.Json;
using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Nutrición vía USDA FoodData Central. Por cada alimento detectado: busca candidatos,
/// elige el mejor por relevancia (SelectorMatchUsda), lee macros por 100 g, estima la
/// porción desde foodPortions (EstimadorPorcion) y escala los macros a esa porción.
/// </summary>
public class ServicioNutricionUsda(HttpClient http) : IServicioNutricion
{
    private const int EnergiaKcal = 1008;
    private const int EnergiaAtwaterGeneral = 2047;
    private const int EnergiaAtwaterEspecifico = 2048;
    private const int Proteina = 1003;
    private const int Carbohidratos = 1005;
    private const int Grasa = 1004;

    public async Task<ResultadoNutricional> ObtenerMacrosAsync(
        IReadOnlyList<string> alimentos, CancellationToken ct = default)
    {
        var resultados = new List<AlimentoNutricional>();
        var crudos = new List<string>();
        var fdcIdsVistos = new HashSet<int>();

        foreach (var alimento in alimentos)
        {
            var url = $"v1/foods/search?query={Uri.EscapeDataString(alimento)}"
                    + "&dataType=Foundation,SR%20Legacy&pageSize=15";

            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) continue;

            var json = await resp.Content.ReadAsStringAsync(ct);
            crudos.Add(json);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("foods", out var foods)
                || foods.GetArrayLength() == 0) continue;

            // Parsear candidatos y elegir el mejor por relevancia.
            var candidatos = new List<CandidatoUsda>();
            foreach (var f in foods.EnumerateArray())
            {
                int fdcId = f.TryGetProperty("fdcId", out var idEl) ? idEl.GetInt32() : 0;
                string desc = f.TryGetProperty("description", out var dEl) ? dEl.GetString() ?? "" : "";
                string tipo = f.TryGetProperty("dataType", out var tEl) ? tEl.GetString() ?? "" : "";
                if (fdcId != 0 && desc.Length > 0) candidatos.Add(new CandidatoUsda(fdcId, desc, tipo));
            }

            var mejor = SelectorMatchUsda.ElegirMejor(candidatos, alimento);
            if (mejor is null || !fdcIdsVistos.Add(mejor.FdcId)) continue;

            // Localizar el JsonElement del match elegido para leer sus macros (por 100 g).
            JsonElement food = default;
            foreach (var f in foods.EnumerateArray())
                if (f.TryGetProperty("fdcId", out var idEl) && idEl.GetInt32() == mejor.FdcId) { food = f; break; }

            decimal Nutriente(params int[] ids)
            {
                if (!food.TryGetProperty("foodNutrients", out var ns)) return 0m;
                foreach (var n in ns.EnumerateArray())
                {
                    if (n.TryGetProperty("nutrientId", out var nidEl)
                        && ids.Contains(nidEl.GetInt32())
                        && n.TryGetProperty("value", out var v)
                        && v.TryGetDecimal(out var dec))
                        return dec;
                }
                return 0m;
            }

            decimal cal100 = Nutriente(EnergiaKcal, EnergiaAtwaterGeneral, EnergiaAtwaterEspecifico);
            decimal pro100 = Nutriente(Proteina);
            decimal car100 = Nutriente(Carbohidratos);
            decimal gra100 = Nutriente(Grasa);

            // Estimar porción desde foodPortions del detalle del alimento (best-effort).
            decimal gramos = EstimadorPorcion.ElegirGramos(await ObtenerGramWeightsAsync(mejor.FdcId, ct));
            decimal factor = gramos / 100m;

            resultados.Add(new AlimentoNutricional(
                Nombre:        mejor.Description,
                Calorias:      Math.Round(cal100 * factor, 2),
                Proteinas:     Math.Round(pro100 * factor, 2),
                Carbohidratos: Math.Round(car100 * factor, 2),
                Grasas:        Math.Round(gra100 * factor, 2),
                Cantidad:      gramos,
                Unidad:        "g"));
        }

        var jsonCrudo = "[" + string.Join(",", crudos) + "]";
        return new ResultadoNutricional(resultados, jsonCrudo);
    }

    /// <summary>
    /// Pide el detalle del alimento y devuelve los gramWeight de sus foodPortions.
    /// Best-effort: ante cualquier fallo devuelve lista vacía (el estimador usa el default).
    /// </summary>
    private async Task<IReadOnlyList<decimal>> ObtenerGramWeightsAsync(int fdcId, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync($"v1/food/{fdcId}", ct);
            if (!resp.IsSuccessStatusCode) return System.Array.Empty<decimal>();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("foodPortions", out var ports)) return System.Array.Empty<decimal>();

            var pesos = new List<decimal>();
            foreach (var p in ports.EnumerateArray())
                if (p.TryGetProperty("gramWeight", out var g) && g.TryGetDecimal(out var dec))
                    pesos.Add(dec);
            return pesos;
        }
        catch
        {
            return System.Array.Empty<decimal>();
        }
    }
}
