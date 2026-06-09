using System.Collections.Concurrent;
using Calorias.Application.Abstractions;

namespace Calorias.Application.Servicios;

/// <summary>
/// Traductor híbrido de nombres de alimentos: diccionario curado → caché → servicio
/// (Cloud Translation) → inglés como último recurso. La caché se siembra con el
/// diccionario y persiste entre peticiones (registrar como singleton).
/// </summary>
public class TraductorAlimentos(IServicioTraduccion servicio)
{
    private readonly ConcurrentDictionary<string, string> cache =
        new(DiccionarioAlimentos.EsPorIngles, StringComparer.OrdinalIgnoreCase);

    public async Task<string> TraducirNombreAsync(string textoIngles, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(textoIngles)) return textoIngles;
        if (cache.TryGetValue(textoIngles, out var hit)) return hit;

        var traducido = await servicio.TraducirAlEspanolAsync(textoIngles, ct);
        var final = string.IsNullOrWhiteSpace(traducido) ? textoIngles : traducido!;
        cache[textoIngles] = final;
        return final;
    }
}
