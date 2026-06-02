using Calorias.Application.Abstractions;
using Google.Cloud.Vision.V1;

namespace Calorias.Infrastructure.Servicios;

public class ServicioVisionGoogle(ImageAnnotatorClient cliente) : IServicioVision
{
    private const float UmbralConfianza = 0.6f;

    public async Task<IReadOnlyList<EtiquetaDetectada>> DetectarAlimentosAsync(
        Stream imagen, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var image = await Image.FromStreamAsync(imagen);
        var etiquetas = await cliente.DetectLabelsAsync(image, maxResults: 15);

        return etiquetas
            .Where(e => e.Score >= UmbralConfianza)
            .Select(e => new EtiquetaDetectada(e.Description, e.Score))
            .ToList();
    }
}
