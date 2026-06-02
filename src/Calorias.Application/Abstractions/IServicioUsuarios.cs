using System.Security.Claims;
using Calorias.Domain.Entities;

namespace Calorias.Application.Abstractions;

public interface IServicioUsuarios
{
    /// <summary>Crea el usuario si no existe; si existe, actualiza datos y último acceso. Idempotente.</summary>
    Task<Usuario> AsegurarUsuarioAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}
