namespace Calorias.Domain.Entities;

public class Usuario
{
    /// <summary>El Id ES el 'sub' de Google (identificador estable y único). No autogenerado.</summary>
    public string Id { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? Nombre { get; set; }
    public string? FotoUrl { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime UltimoAccesoEn { get; set; } = DateTime.UtcNow;

    public ICollection<RegistroComida> Registros { get; set; } = new List<RegistroComida>();
}
