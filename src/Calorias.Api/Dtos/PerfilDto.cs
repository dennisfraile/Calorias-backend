namespace Calorias.Api.Dtos;

/// <summary>Macros objetivo en gramos.</summary>
public record MacrosDto(int ProteinaG, int CarbosG, int GrasasG);

/// <summary>
/// Perfil + meta calculada. Cuando el perfil está incompleto, los campos calculados
/// (MetaCalorias/Macros/Tmb/Tdee) van en null. Serializa en camelCase.
/// </summary>
public record PerfilDto(
    bool PerfilCompleto,
    string? Sexo,
    string? FechaNacimiento,
    int? AlturaCm,
    decimal? PesoKg,
    string? NivelActividad,
    string? Objetivo,
    decimal? RitmoKgSemana,
    int MetaProteinaPct,
    int MetaCarbosPct,
    int MetaGrasasPct,
    int? MetaCaloriasOverride,
    int? MetaCalorias,
    bool MetaTopada,
    MacrosDto? Macros,
    int? Tmb,
    int? Tdee);

/// <summary>Body de PUT /api/perfil. Todos los campos opcionales (reemplazo de perfil).</summary>
public record ActualizarPerfilDto(
    string? Sexo,
    string? FechaNacimiento,
    int? AlturaCm,
    decimal? PesoKg,
    string? NivelActividad,
    string? Objetivo,
    decimal? RitmoKgSemana,
    int? MetaProteinaPct,
    int? MetaCarbosPct,
    int? MetaGrasasPct,
    int? MetaCaloriasOverride);
