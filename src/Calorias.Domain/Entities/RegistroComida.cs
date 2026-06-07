namespace Calorias.Domain.Entities;

public class RegistroComida
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string UsuarioId { get; set; } = default!;
    public Usuario Usuario { get; set; } = default!;

    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    /// <summary>Día local del usuario (lo envía el cliente). Clave de agrupación diaria.</summary>
    public DateOnly FechaLocal { get; set; }

    /// <summary>Tipo de comida (desayuno/almuerzo/cena/snack).</summary>
    public TipoComida Tipo { get; set; }

    public string? UrlImagen { get; set; }

    // Totales agregados (desnormalizados para lectura rápida en el dashboard)
    public decimal CaloriasTotales      { get; set; }
    public decimal ProteinasTotales     { get; set; }
    public decimal CarbohidratosTotales { get; set; }
    public decimal GrasasTotales        { get; set; }

    // Payloads CRUDOS para auditoría/respaldo: JSON literal de cada API en columnas jsonb.
    public string? PayloadVisionJson      { get; set; }
    public string? PayloadNutritionixJson { get; set; }

    public ICollection<DetalleComida> Detalles { get; set; } = new List<DetalleComida>();
}

public class DetalleComida
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid RegistroComidaId { get; set; }
    public RegistroComida RegistroComida { get; set; } = default!;

    public string NombreAlimento { get; set; } = default!;
    public decimal Cantidad      { get; set; }
    public string UnidadMedida   { get; set; } = default!;
    public decimal Calorias      { get; set; }
    public decimal Proteinas     { get; set; }
    public decimal Carbohidratos { get; set; }
    public decimal Grasas        { get; set; }
    public double  ConfianzaDeteccion { get; set; }
}
