using System.Net.Http.Json;
using System.Text.Json;
using Calorias.Application.Abstractions;

namespace Calorias.Infrastructure.Servicios;

public class ServicioNutritionix(HttpClient http) : IServicioNutricion
{
    public async Task<ResultadoNutricional> ObtenerMacrosAsync(
        string consulta, CancellationToken ct = default)
    {
        // Endpoint Natural Language de Nutritionix
        var resp = await http.PostAsJsonAsync(
            "v2/natural/nutrients", new { query = consulta }, ct);
        resp.EnsureSuccessStatusCode();

        var jsonCrudo = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(jsonCrudo);

        var alimentos = doc.RootElement.GetProperty("foods").EnumerateArray()
            .Select(f => new AlimentoNutricional(
                Nombre:        f.GetProperty("food_name").GetString()!,
                Calorias:      f.GetProperty("nf_calories").GetDecimal(),
                Proteinas:     f.GetProperty("nf_protein").GetDecimal(),
                Carbohidratos: f.GetProperty("nf_total_carbohydrate").GetDecimal(),
                Grasas:        f.GetProperty("nf_total_fat").GetDecimal(),
                Cantidad:      f.GetProperty("serving_qty").GetDecimal(),
                Unidad:        f.GetProperty("serving_unit").GetString()!))
            .ToList();

        return new ResultadoNutricional(alimentos, jsonCrudo);
    }
}
