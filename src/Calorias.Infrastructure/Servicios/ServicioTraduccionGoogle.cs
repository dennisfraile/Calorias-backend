using Calorias.Application.Abstractions;
using Google.Cloud.Translation.V2;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Traducción EN→ES vía Google Cloud Translation v2. Usa la misma credencial ADC
/// (GOOGLE_APPLICATION_CREDENTIALS) que Vision. Best-effort: ante error devuelve null
/// para que el facade caiga al diccionario/inglés.
/// </summary>
public class ServicioTraduccionGoogle(TranslationClient cliente) : IServicioTraduccion
{
    public async Task<string?> TraducirAlEspanolAsync(string textoIngles, CancellationToken ct = default)
    {
        try
        {
            var r = await cliente.TranslateTextAsync(
                text: textoIngles, targetLanguage: "es", sourceLanguage: "en", cancellationToken: ct);
            return r?.TranslatedText;
        }
        catch
        {
            return null;
        }
    }
}
