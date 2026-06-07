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

        // 2) Tomamos los alimentos detectados (máx. 5) para consultar la nutrición
        var alimentos = etiquetas.Take(5).Select(e => e.Descripcion).ToList();
        logger.LogInformation("Consultando USDA FoodData Central con: {Alimentos}",
            string.Join(", ", alimentos));

        var nutricional = await nutricion.ObtenerMacrosAsync(alimentos, ct);

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
