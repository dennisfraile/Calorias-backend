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

    // --- Perfil físico (nullable hasta completar onboarding) ---
    public Sexo? Sexo { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
    public int? AlturaCm { get; set; }
    public decimal? PesoKg { get; set; }
    public NivelActividad? NivelActividad { get; set; }
    public Objetivo? Objetivo { get; set; }

    /// <summary>kg por semana a perder/ganar (se ignora en Mantener). Ajusta las calorías.</summary>
    public decimal? RitmoKgSemana { get; set; }

    /// <summary>Si está, manda sobre la meta calculada.</summary>
    public int? MetaCaloriasOverride { get; set; }

    // --- Split de macros (porcentajes del total calórico; suman ~100) ---
    public int MetaProteinaPct { get; set; } = 30;
    public int MetaCarbosPct { get; set; } = 40;
    public int MetaGrasasPct { get; set; } = 30;

    public ICollection<RegistroComida> Registros { get; set; } = new List<RegistroComida>();
}
