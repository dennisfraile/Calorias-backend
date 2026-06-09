namespace Calorias.Application.Abstractions;

/// <summary>Traduce un texto del inglés al español. Devuelve null si no puede traducir.</summary>
public interface IServicioTraduccion
{
    Task<string?> TraducirAlEspanolAsync(string textoIngles, CancellationToken ct = default);
}
