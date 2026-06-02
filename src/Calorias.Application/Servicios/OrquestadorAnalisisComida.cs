using System.Text.Json;
using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Calorias.Application.Servicios;

public class OrquestadorAnalisisComida(
    IServicioVision vision,
    IServicioNutricion nutricion,
    ILogger<OrquestadorAnalisisComida> logger)
{
    public async Task<RegistroComida> AnalizarAsync(
        Stream imagen, string usuarioId, CancellationToken ct = default)
    {
        // 1) Vision detecta etiquetas
        var etiquetas = await vision.DetectarAlimentosAsync(imagen, ct);
        if (etiquetas.Count == 0)
            throw new InvalidOperationException("No se detectaron alimentos en la imagen.");

        // 2) Construimos una consulta en lenguaje natural para Nutritionix
        var consulta = string.Join(" and ", etiquetas.Take(5).Select(e => e.Descripcion));
        logger.LogInformation("Consultando Nutritionix con: {Consulta}", consulta);

        var nutricional = await nutricion.ObtenerMacrosAsync(consulta, ct);

        // 3) Componemos la entidad de dominio + payloads crudos (jsonb) para auditoría
        var registro = new RegistroComida
        {
            UsuarioId = usuarioId,
            // Payloads crudos (jsonb): snapshot de Vision serializado + JSON literal de Nutritionix
            PayloadVisionJson = JsonSerializer.Serialize(etiquetas),
            PayloadNutritionixJson = nutricional.JsonCrudo,
            Detalles = nutricional.Alimentos.Select(a => new DetalleComida
            {
                NombreAlimento = a.Nombre,
                Calorias = a.Calorias,
                Proteinas = a.Proteinas,
                Carbohidratos = a.Carbohidratos,
                Grasas = a.Grasas,
                Cantidad = a.Cantidad,
                UnidadMedida = a.Unidad,
                ConfianzaDeteccion = etiquetas
                    .FirstOrDefault(e => a.Nombre.Contains(e.Descripcion,
                        StringComparison.OrdinalIgnoreCase))?.Confianza ?? 0
            }).ToList()
        };

        // 4) Totales agregados
        registro.CaloriasTotales      = registro.Detalles.Sum(d => d.Calorias);
        registro.ProteinasTotales     = registro.Detalles.Sum(d => d.Proteinas);
        registro.CarbohidratosTotales = registro.Detalles.Sum(d => d.Carbohidratos);
        registro.GrasasTotales        = registro.Detalles.Sum(d => d.Grasas);

        return registro;
    }
}
