using System.Security.Claims;
using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Infrastructure.Servicios;

public class ServicioUsuarios(CaloriasDbContext db) : IServicioUsuarios
{
    public async Task<Usuario> AsegurarUsuarioAsync(
        ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? principal.FindFirst("sub")?.Value
                  ?? throw new UnauthorizedAccessException("Token sin 'sub'.");

        var email  = principal.FindFirst(ClaimTypes.Email)?.Value ?? "";
        var nombre = principal.FindFirst(ClaimTypes.Name)?.Value
                     ?? principal.FindFirst("name")?.Value;
        var foto   = principal.FindFirst("picture")?.Value;
        var ahora  = DateTime.UtcNow;

        // UPSERT atómico. ON CONFLICT en la PK ("Id") resuelve inserciones simultáneas:
        // gana un INSERT, el resto cae al DO UPDATE. RETURNING devuelve la fila final.
        // ToListAsync ejecuta el SQL verbatim (sin envolverlo en subconsulta).
        var usuarios = await db.Usuarios
            .FromSqlInterpolated($"""
                INSERT INTO "Usuarios" ("Id", "Email", "Nombre", "FotoUrl", "CreadoEn", "UltimoAccesoEn")
                VALUES ({sub}, {email}, {nombre}, {foto}, {ahora}, {ahora})
                ON CONFLICT ("Id") DO UPDATE SET
                    "Email"          = EXCLUDED."Email",
                    "Nombre"         = EXCLUDED."Nombre",
                    "FotoUrl"        = EXCLUDED."FotoUrl",
                    "UltimoAccesoEn" = EXCLUDED."UltimoAccesoEn"
                RETURNING *;
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        return usuarios.Single();
    }
}
