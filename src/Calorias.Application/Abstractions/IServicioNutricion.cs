namespace Calorias.Application.Abstractions;

public interface IServicioNutricion
{
    Task<ResultadoNutricional> ObtenerMacrosAsync(
        string consultaLenguajeNatural, CancellationToken ct = default);
}

public record ResultadoNutricional(
    IReadOnlyList<AlimentoNutricional> Alimentos, string JsonCrudo);

public record AlimentoNutricional(
    string Nombre, decimal Calorias, decimal Proteinas,
    decimal Carbohidratos, decimal Grasas, decimal Cantidad, string Unidad);
