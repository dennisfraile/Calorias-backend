namespace Calorias.Domain.Entities;

/// <summary>
/// Rollup pre-agregado por usuario y día local. Es la única granularidad mantenida
/// incrementalmente; los períodos más gruesos se calculan con GROUP BY sobre esta tabla.
/// </summary>
public class ResumenDiario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UsuarioId { get; set; } = default!;
    public DateOnly FechaLocal { get; set; }

    public decimal CaloriasTotal { get; set; }
    public decimal ProteinasTotal { get; set; }
    public decimal CarbosTotal { get; set; }
    public decimal GrasasTotal { get; set; }
    public int NumComidas { get; set; }
}
