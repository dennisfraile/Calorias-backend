using System.Security.Claims;
using Calorias.Api.Dtos;
using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Calorias.Domain.Servicios;
using Calorias.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/perfil")]
public class PerfilController(IServicioUsuarios usuarios, CaloriasDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PerfilDto>> Obtener(CancellationToken ct)
    {
        // AsegurarUsuarioAsync hace upsert idempotente y devuelve la fila (con perfil).
        var usuario = await usuarios.AsegurarUsuarioAsync(User, ct);
        return Ok(Mapear(usuario));
    }

    [HttpPut]
    public async Task<ActionResult<PerfilDto>> Actualizar(
        [FromBody] ActualizarPerfilDto body, CancellationToken ct)
    {
        // Garantiza la existencia del usuario (FK) antes de actualizar.
        await usuarios.AsegurarUsuarioAsync(User, ct);

        var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();

        if (!TryParseEnum<Sexo>(body.Sexo, out var sexo)) return BadRequest("Sexo no válido.");
        if (!TryParseEnum<NivelActividad>(body.NivelActividad, out var nivel))
            return BadRequest("NivelActividad no válido.");
        if (!TryParseEnum<Objetivo>(body.Objetivo, out var objetivo))
            return BadRequest("Objetivo no válido.");

        DateOnly? fechaNac = null;
        if (!string.IsNullOrWhiteSpace(body.FechaNacimiento))
        {
            if (!DateOnly.TryParse(body.FechaNacimiento, out var fn))
                return BadRequest("FechaNacimiento no válida (use YYYY-MM-DD).");
            fechaNac = fn;
        }

        if (body.AlturaCm is < 0 or > 300) return BadRequest("AlturaCm fuera de rango.");
        if (body.PesoKg is < 0 or > 999) return BadRequest("PesoKg fuera de rango.");

        // Entidad rastreada para actualizar.
        var u = await db.Usuarios.FirstAsync(x => x.Id == usuarioId, ct);
        u.Sexo = sexo;
        u.FechaNacimiento = fechaNac;
        u.AlturaCm = body.AlturaCm;
        u.PesoKg = body.PesoKg;
        u.NivelActividad = nivel;
        u.Objetivo = objetivo;
        u.RitmoKgSemana = body.RitmoKgSemana;
        u.MetaCaloriasOverride = body.MetaCaloriasOverride;
        u.MetaProteinaPct = body.MetaProteinaPct ?? u.MetaProteinaPct;
        u.MetaCarbosPct = body.MetaCarbosPct ?? u.MetaCarbosPct;
        u.MetaGrasasPct = body.MetaGrasasPct ?? u.MetaGrasasPct;

        await db.SaveChangesAsync(ct);
        return Ok(Mapear(u));
    }

    private static PerfilDto Mapear(Usuario u)
    {
        bool completo = u.Sexo is not null && u.FechaNacimiento is not null
            && u.AlturaCm is not null && u.PesoKg is not null
            && u.NivelActividad is not null && u.Objetivo is not null;

        int? metaCalorias = null;
        bool metaTopada = false;
        MacrosDto? macros = null;
        int? tmb = null, tdee = null;

        if (completo)
        {
            var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
            var edad = CalcularEdad(u.FechaNacimiento!.Value, hoy);
            var r = CalculadoraObjetivos.Calcular(
                u.Sexo!.Value, edad, u.AlturaCm!.Value, u.PesoKg!.Value,
                u.NivelActividad!.Value, u.Objetivo!.Value, u.RitmoKgSemana ?? 0m,
                u.MetaCaloriasOverride, u.MetaProteinaPct, u.MetaCarbosPct, u.MetaGrasasPct);

            metaCalorias = r.MetaCalorias;
            metaTopada = r.MetaTopada;
            macros = new MacrosDto(r.ProteinaG, r.CarbosG, r.GrasasG);
            tmb = r.Tmb;
            tdee = r.Tdee;
        }

        return new PerfilDto(
            completo,
            u.Sexo?.ToString(),
            u.FechaNacimiento?.ToString("yyyy-MM-dd"),
            u.AlturaCm,
            u.PesoKg,
            u.NivelActividad?.ToString(),
            u.Objetivo?.ToString(),
            u.RitmoKgSemana,
            u.MetaProteinaPct,
            u.MetaCarbosPct,
            u.MetaGrasasPct,
            u.MetaCaloriasOverride,
            metaCalorias,
            metaTopada,
            macros,
            tmb,
            tdee);
    }

    private static int CalcularEdad(DateOnly nacimiento, DateOnly hoy)
    {
        var edad = hoy.Year - nacimiento.Year;
        if (nacimiento > hoy.AddYears(-edad)) edad--;
        return edad;
    }

    /// <summary>Parsea un enum opcional. Vacío/null => null (válido). Texto inválido => false.</summary>
    private static bool TryParseEnum<TEnum>(string? value, out TEnum? result) where TEnum : struct, Enum
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            result = parsed;
            return true;
        }
        return false;
    }
}
