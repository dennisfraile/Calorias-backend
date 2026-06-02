using System.Security.Claims;
using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Calorias.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Calorias.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/comidas")]
public class ComidasController(
    OrquestadorAnalisisComida orquestador,
    IServicioUsuarios usuarios,
    CaloriasDbContext db) : ControllerBase
{
    [HttpPost("analizar")]
    [RequestSizeLimit(10_000_000)] // 10 MB
    public async Task<IActionResult> Analizar(IFormFile foto, CancellationToken ct)
    {
        if (foto is null || foto.Length == 0)
            return BadRequest("Imagen requerida.");

        // Upsert idempotente del usuario desde los claims del token de Google.
        // Garantiza la integridad referencial (FK) antes de guardar la comida.
        var usuario = await usuarios.AsegurarUsuarioAsync(User, ct);

        await using var stream = foto.OpenReadStream();
        var registro = await orquestador.AnalizarAsync(stream, usuario.Id, ct);

        db.Registros.Add(registro);
        await db.SaveChangesAsync(ct);

        return Ok(registro);
    }
}
