using System.Security.Claims;
using Calorias.Api.Dtos;
using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/comidas")]
public class ComidasController(
    OrquestadorAnalisisComida orquestador,
    IServicioUsuarios usuarios,
    IServicioResumenDiario resumen,
    CaloriasDbContext db) : ControllerBase
{
    [HttpPost("analizar")]
    [RequestSizeLimit(10_000_000)] // 10 MB
    public async Task<IActionResult> Analizar(
        [FromForm] IFormFile foto,
        [FromForm] TipoComida? tipo,
        [FromForm] DateOnly? fechaLocal,
        CancellationToken ct)
    {
        if (foto is null || foto.Length == 0)
            return BadRequest("Imagen requerida.");
        if (tipo is null || !Enum.IsDefined(tipo.Value))
            return BadRequest("Tipo de comida no válido.");
        if (fechaLocal is null)
            return BadRequest("fechaLocal es requerido.");

        // Upsert idempotente del usuario desde los claims del token de Google.
        // Garantiza la integridad referencial (FK) antes de guardar la comida.
        var usuario = await usuarios.AsegurarUsuarioAsync(User, ct);

        await using var stream = foto.OpenReadStream();
        var registro = await orquestador.AnalizarAsync(stream, usuario.Id, ct);
        registro.Tipo = tipo.Value;
        registro.FechaLocal = fechaLocal.Value;

        // Guardado de la comida + recálculo del rollup en la misma transacción.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Registros.Add(registro);
        await db.SaveChangesAsync(ct);
        await resumen.RecalcularDiaAsync(usuario.Id, fechaLocal.Value, ct);
        await tx.CommitAsync(ct);

        // Aplanamos a DTO: devolver la entidad EF cicla (Detalles → RegistroComida → …).
        var dto = new AnalisisComidaDto(
            registro.Id,
            registro.Tipo.ToString(),
            registro.FechaLocal.ToString("yyyy-MM-dd"),
            registro.FechaRegistro.ToString("o"),
            registro.CaloriasTotales,
            registro.ProteinasTotales,
            registro.CarbohidratosTotales,
            registro.GrasasTotales,
            registro.Detalles.Select(d => new DetalleComidaDto(
                d.NombreAlimento, d.Calorias, d.Proteinas,
                d.Carbohidratos, d.Grasas, d.Cantidad, d.UnidadMedida)).ToList());

        return Ok(dto);
    }

    /// <summary>
    /// Historial de comidas del usuario autenticado, más reciente primero.
    /// Devuelve un DTO plano pensado para la pantalla data-driven (Mythik).
    /// </summary>
    [HttpGet("historial")]
    public async Task<ActionResult<IReadOnlyList<HistorialComidaDto>>> Historial(CancellationToken ct)
    {
        // 'sub' de Google (JwtBearer lo mapea a NameIdentifier). Lectura: no hace upsert.
        var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(usuarioId))
            return Unauthorized();

        // Proyección a tipos primitivos que EF sabe traducir a SQL.
        var filas = await db.Registros
            .AsNoTracking()
            .Where(r => r.UsuarioId == usuarioId)
            .OrderByDescending(r => r.FechaRegistro)
            .Select(r => new
            {
                r.Id,
                r.FechaRegistro,
                r.CaloriasTotales,
                r.ProteinasTotales,
                r.CarbohidratosTotales,
                r.GrasasTotales,
                Nombres = r.Detalles.Select(d => d.NombreAlimento).ToList(),
            })
            .ToListAsync(ct);

        // El formato de nombre/hora se hace en memoria (no es traducible a SQL).
        var historial = filas
            .Select(r => new HistorialComidaDto(
                r.Id,
                ComponerNombre(r.Nombres),
                r.FechaRegistro.ToString("yyyy-MM-dd"),
                r.FechaRegistro.ToString("HH:mm"),
                r.CaloriasTotales,
                r.ProteinasTotales,
                r.CarbohidratosTotales,
                r.GrasasTotales))
            .ToList();

        return Ok(historial);
    }

    private static string ComponerNombre(IReadOnlyList<string> nombres)
    {
        if (nombres.Count == 0) return "Comida";
        var visibles = string.Join(", ", nombres.Take(3));
        return nombres.Count > 3 ? $"{visibles}…" : visibles;
    }
}
