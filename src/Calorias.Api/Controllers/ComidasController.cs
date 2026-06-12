using System.Security.Claims;
using Calorias.Api.Dtos;
using Calorias.Application.Abstractions;
using Calorias.Application.Resultados;
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
    IServicioResumenPeriodos resumenPeriodos,
    IServicioCorreccionPorciones correccion,
    IServicioEtiquetaNutricional etiquetas,
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
        var dto = ADto(registro);
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

    /// <summary>
    /// Resumen comparativo (actual vs período anterior) para el dashboard.
    /// periodo ∈ {diario, semanal, mensual, trimestral, semestral, anual}.
    /// ancla (YYYY-MM-DD) define el período; si falta, se usa la fecha UTC de hoy.
    /// </summary>
    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenPeriodos>> Resumen(
        [FromQuery] string periodo,
        [FromQuery] DateOnly? ancla,
        CancellationToken ct)
    {
        var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(usuarioId))
            return Unauthorized();

        if (!Enum.TryParse<Periodo>(periodo, ignoreCase: true, out var p) || !Enum.IsDefined(p))
            return BadRequest("periodo no válido.");

        var dia = ancla ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var res = await resumenPeriodos.ObtenerAsync(usuarioId, p, dia, ct);
        return Ok(res);
    }

    /// <summary>
    /// Corrige las porciones (gramos) de los detalles de una comida propia y recalcula
    /// totales + rollup. cantidadG = 0 elimina ese detalle. Devuelve la comida actualizada.
    /// </summary>
    [HttpPut("{id:guid}/porciones")]
    public async Task<ActionResult<AnalisisComidaDto>> CorregirPorciones(
        Guid id, [FromBody] CorreccionPorcionesDto cuerpo, CancellationToken ct)
    {
        var usuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(usuarioId)) return Unauthorized();
        if (cuerpo?.Detalles is null || cuerpo.Detalles.Count == 0)
            return BadRequest("Sin correcciones.");

        var correcciones = cuerpo.Detalles
            .Select(d => new CorreccionDetalle(d.DetalleId, d.CantidadG))
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var registro = await correccion.CorregirAsync(usuarioId, id, correcciones, ct);
        if (registro is null) { await tx.RollbackAsync(ct); return NotFound(); }
        await tx.CommitAsync(ct);

        return Ok(ADto(registro));
    }

    /// <summary>
    /// Lee una etiqueta nutricional (Gemini) y devuelve sus datos por porción. NO guarda.
    /// </summary>
    [HttpPost("leer-etiqueta")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<EtiquetaNutricionalDto>> LeerEtiqueta(
        [FromForm] IFormFile foto, CancellationToken ct)
    {
        if (foto is null || foto.Length == 0) return BadRequest("Imagen requerida.");

        await using var stream = foto.OpenReadStream();
        var etq = await etiquetas.LeerEtiquetaAsync(stream, foto.ContentType ?? "image/jpeg", ct);
        if (etq is null)
            return BadRequest("No se pudo leer la etiqueta; enfoca el panel nutricional.");

        return Ok(new EtiquetaNutricionalDto(
            etq.NombreProducto, etq.TamPorcion, etq.UnidadPorcion, etq.PorcionesPorEnvase,
            etq.Base == BaseMedida.Cien ? "cien" : "porcion",
            etq.CaloriasPorBase, etq.ProteinaPorBase, etq.CarbosPorBase, etq.GrasasPorBase));
    }

    /// <summary>
    /// Guarda una comida a partir de los datos leídos de una etiqueta + porciones consumidas.
    /// </summary>
    [HttpPost("guardar-etiqueta")]
    public async Task<ActionResult<AnalisisComidaDto>> GuardarEtiqueta(
        [FromBody] GuardarEtiquetaDto cuerpo, CancellationToken ct)
    {
        if (cuerpo is null) return BadRequest("Cuerpo requerido.");
        if (!Enum.TryParse<TipoComida>(cuerpo.Tipo, ignoreCase: true, out var tipo) || !Enum.IsDefined(tipo))
            return BadRequest("Tipo de comida no válido.");
        if (cuerpo.Porciones <= 0m) return BadRequest("Porciones debe ser > 0.");

        var usuario = await usuarios.AsegurarUsuarioAsync(User, ct);

        var baseMedida = string.Equals(cuerpo.Base?.Trim(), "cien", StringComparison.OrdinalIgnoreCase)
            ? BaseMedida.Cien : BaseMedida.Porcion;
        var etq = new EtiquetaNutricional(
            cuerpo.NombreProducto, cuerpo.TamPorcion, cuerpo.UnidadPorcion, null,
            baseMedida,
            cuerpo.CaloriasPorBase, cuerpo.ProteinaPorBase, cuerpo.CarbosPorBase, cuerpo.GrasasPorBase);
        var registro = RegistroDesdeEtiqueta.Construir(etq, cuerpo.Porciones, usuario.Id, tipo, cuerpo.FechaLocal);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Registros.Add(registro);
        await db.SaveChangesAsync(ct);
        await resumen.RecalcularDiaAsync(usuario.Id, cuerpo.FechaLocal, ct);
        await tx.CommitAsync(ct);

        return Ok(ADto(registro));
    }

    private static AnalisisComidaDto ADto(RegistroComida r) => new(
        r.Id,
        r.Tipo.ToString(),
        r.FechaLocal.ToString("yyyy-MM-dd"),
        r.FechaRegistro.ToString("o"),
        r.CaloriasTotales,
        r.ProteinasTotales,
        r.CarbohidratosTotales,
        r.GrasasTotales,
        r.Detalles.Select(d => new DetalleComidaDto(
            d.Id, d.NombreAlimento, d.Calorias, d.Proteinas,
            d.Carbohidratos, d.Grasas, d.Cantidad, d.UnidadMedida)).ToList());

    private static string ComponerNombre(IReadOnlyList<string> nombres)
    {
        if (nombres.Count == 0) return "Comida";
        var visibles = string.Join(", ", nombres.Take(3));
        return nombres.Count > 3 ? $"{visibles}…" : visibles;
    }
}
