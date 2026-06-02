namespace Calorias.Application.Abstractions;

public interface IServicioVision
{
    Task<IReadOnlyList<EtiquetaDetectada>> DetectarAlimentosAsync(
        Stream imagen, CancellationToken ct = default);
}

public record EtiquetaDetectada(string Descripcion, float Confianza);
