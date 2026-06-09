using System.Text;
using System.Text.Json;
using Calorias.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Lee una etiqueta nutricional con Gemini (Google AI Studio, free tier) vía REST:
/// envía la imagen inline + un prompt que pide JSON estructurado (responseSchema) con los
/// valores POR PORCIÓN ya en kcal. Best-effort: ante cualquier fallo devuelve null.
/// API key en config 'Gemini:ApiKey'. Modelo: gemini-2.0-flash.
/// </summary>
public class ServicioEtiquetaGemini(HttpClient http, IConfiguration config) : IServicioEtiquetaNutricional
{
    private const string Modelo = "gemini-2.0-flash";

    private const string Prompt =
        "Eres un extractor de etiquetas nutricionales. Lee la tabla nutricional de la imagen y " +
        "devuelve SOLO los datos POR PORCIÓN. Convierte la energía a kcal (si está en kJ, divídela " +
        "entre 4.184). 'tamPorcion' es el tamaño de una porción y 'unidadPorcion' su unidad ('g' o 'mL'). " +
        "Incluye 'porcionesPorEnvase' si aparece. Si la imagen NO es una etiqueta nutricional legible, " +
        "pon 0 en caloriasPorPorcion. Responde en el idioma del producto para 'nombreProducto'.";

    public async Task<EtiquetaNutricional?> LeerEtiquetaAsync(
        Stream imagen, string mimeType, CancellationToken ct = default)
    {
        try
        {
            using var ms = new MemoryStream();
            await imagen.CopyToAsync(ms, ct);
            var base64 = Convert.ToBase64String(ms.ToArray());

            var apiKey = config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey)) return null;

            var cuerpo = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = Prompt },
                            new { inlineData = new { mimeType, data = base64 } },
                        },
                    },
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            nombreProducto = new { type = "STRING", nullable = true },
                            tamPorcion = new { type = "NUMBER" },
                            unidadPorcion = new { type = "STRING" },
                            porcionesPorEnvase = new { type = "NUMBER", nullable = true },
                            caloriasPorPorcion = new { type = "NUMBER" },
                            proteinaPorPorcion = new { type = "NUMBER" },
                            carbosPorPorcion = new { type = "NUMBER" },
                            grasasPorPorcion = new { type = "NUMBER" },
                        },
                        required = new[]
                        {
                            "tamPorcion", "unidadPorcion", "caloriasPorPorcion",
                            "proteinaPorPorcion", "carbosPorPorcion", "grasasPorPorcion",
                        },
                    },
                },
            };

            var json = JsonSerializer.Serialize(cuerpo);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"models/{Modelo}:generateContent?key={Uri.EscapeDataString(apiKey)}";

            using var resp = await http.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var respJson = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(respJson);

            // candidates[0].content.parts[0].text contiene el JSON pedido (responseMimeType=application/json).
            if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
                return null;
            var texto = cands[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(texto)) return null;

            using var datos = JsonDocument.Parse(texto);
            var r = datos.RootElement;

            decimal Num(string prop) =>
                r.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number
                    ? el.GetDecimal() : 0m;
            decimal? NumNull(string prop) =>
                r.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number
                    ? el.GetDecimal() : (decimal?)null;
            string? Str(string prop) =>
                r.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() : null;

            var calorias = Num("caloriasPorPorcion");
            if (calorias <= 0m) return null; // no es etiqueta legible

            return new EtiquetaNutricional(
                NombreProducto: Str("nombreProducto"),
                TamPorcion: Num("tamPorcion") > 0m ? Num("tamPorcion") : 100m,
                UnidadPorcion: Str("unidadPorcion") ?? "g",
                PorcionesPorEnvase: NumNull("porcionesPorEnvase"),
                CaloriasPorPorcion: calorias,
                ProteinaPorPorcion: Num("proteinaPorPorcion"),
                CarbosPorPorcion: Num("carbosPorPorcion"),
                GrasasPorPorcion: Num("grasasPorPorcion"));
        }
        catch
        {
            return null;
        }
    }
}
