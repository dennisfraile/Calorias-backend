# Fase B — Perfil físico y metas (CalculadoraObjetivos TDEE + /api/perfil + pantalla Perfil) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que el usuario complete un perfil físico (sexo, edad, altura, peso, actividad, objetivo, ritmo) y la app calcule su meta de calorías (TDEE Mifflin-St Jeor con ajuste por objetivo, piso de seguridad y override) más metas de macros, expuesto por `GET/PUT /api/perfil` y editable en una pantalla Perfil accesible desde un avatar en la esquina.

**Architecture:** .NET 10 Clean Architecture. Se añaden 3 enums y campos de perfil (nullables) a `Usuario`, un servicio **puro de dominio** `CalculadoraObjetivos` (sin estado, sin DB → unit-testeable), un `PerfilController` (GET/PUT) que persiste el perfil y devuelve la meta recalculada. El upsert existente de `ServicioUsuarios` (que sólo actualiza email/foto/último-acceso en el `DO UPDATE`) **preserva** los campos de perfil al re-loguear. El frontend añade `api/perfil.ts`, una pantalla `PerfilScreen` (RN clásico) y un avatar en la cabecera de `App.tsx` que la abre.

**Tech Stack:** C# / EF Core 10 (Npgsql/PostgreSQL), xUnit (proyecto `Calorias.Tests` ya existente; `CalculadoraObjetivos` se testea sin DB), Expo/React Native (TypeScript) en el frontend.

**Spec:** `docs/superpowers/specs/2026-06-07-comidas-objetivos-dashboards-design.md` (secciones 3.1, 3.2, 4, 6.1, 6.2, 7).

**Rutas base:**
- Backend: `c:\Users\LENOVO\Documents\Portafolio\App Calorias\backend`
- Frontend: `c:\Users\LENOVO\Documents\Portafolio\App Calorias\frontend`

> **Nota EF:** los comandos `dotnet ef` requieren `$env:ASPNETCORE_ENVIRONMENT='Development'` (la cadena de conexión vive en user-secrets). **Parar el backend** antes de migrar (bloquea las DLLs). Si falta el tool: `dotnet tool install --global dotnet-ef`.

> **Nota serialización:** ASP.NET Core serializa con camelCase por defecto, así que el record `PerfilDto.PerfilCompleto` llega al cliente como `perfilCompleto` (coincide con la spec y con cómo el frontend ya consume `caloriasTotales`).

## File Structure

**Backend (repo `backend`):**
- `src/Calorias.Domain/Entities/Sexo.cs` — enum `Sexo` (nuevo).
- `src/Calorias.Domain/Entities/NivelActividad.cs` — enum `NivelActividad` (nuevo).
- `src/Calorias.Domain/Entities/Objetivo.cs` — enum `Objetivo` (nuevo).
- `src/Calorias.Domain/Entities/Usuario.cs` — campos de perfil (modificar).
- `src/Calorias.Domain/Servicios/CalculadoraObjetivos.cs` — cálculo TDEE puro (nuevo).
- `tests/Calorias.Tests/CalculadoraObjetivosTests.cs` — tests del cálculo (nuevo).
- `src/Calorias.Infrastructure/Persistence/CaloriasDbContext.cs` — mapping de los campos (modificar).
- `src/Calorias.Infrastructure/Migrations/*_PerfilUsuario.cs` — migración (generado).
- `src/Calorias.Api/Dtos/PerfilDto.cs` — DTOs de perfil (nuevo).
- `src/Calorias.Api/Controllers/PerfilController.cs` — GET/PUT `/api/perfil` (nuevo).

**Frontend (repo `frontend`):**
- `src/config.ts` — añadir `PERFIL_URL` (modificar).
- `src/api/perfil.ts` — cliente `getPerfil`/`putPerfil` (nuevo).
- `src/screens/PerfilScreen.tsx` — formulario de perfil (nuevo).
- `App.tsx` — cabecera con avatar + navegación a Perfil (modificar).

---

### Task 1: Enums de perfil en el dominio

**Files:**
- Create: `backend/src/Calorias.Domain/Entities/Sexo.cs`
- Create: `backend/src/Calorias.Domain/Entities/NivelActividad.cs`
- Create: `backend/src/Calorias.Domain/Entities/Objetivo.cs`

- [ ] **Step 1: Crear `Sexo.cs`**

```csharp
namespace Calorias.Domain.Entities;

public enum Sexo
{
    Masculino,
    Femenino
}
```

- [ ] **Step 2: Crear `NivelActividad.cs`**

```csharp
namespace Calorias.Domain.Entities;

/// <summary>Nivel de actividad física; cada uno mapea a un factor multiplicador del TDEE.</summary>
public enum NivelActividad
{
    Sedentario,
    Ligero,
    Moderado,
    Activo,
    MuyActivo
}
```

- [ ] **Step 3: Crear `Objetivo.cs`**

```csharp
namespace Calorias.Domain.Entities;

public enum Objetivo
{
    Perder,
    Mantener,
    Ganar
}
```

- [ ] **Step 4: Compilar el dominio**

Run: `dotnet build "backend/src/Calorias.Domain/Calorias.Domain.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Domain/Entities/Sexo.cs src/Calorias.Domain/Entities/NivelActividad.cs src/Calorias.Domain/Entities/Objetivo.cs
git -C backend commit -m "feat(domain): enums Sexo, NivelActividad y Objetivo"
```

---

### Task 2: Campos de perfil en `Usuario`

**Files:**
- Modify: `backend/src/Calorias.Domain/Entities/Usuario.cs`

- [ ] **Step 1: Añadir los campos de perfil**

En `backend/src/Calorias.Domain/Entities/Usuario.cs`, justo después de la línea
`public DateTime UltimoAccesoEn { get; set; } = DateTime.UtcNow;` añadir:

```csharp

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
```

> Nota: las propiedades enum se llaman igual que su tipo (`Sexo Sexo`, etc.); es válido en C#.

- [ ] **Step 2: Compilar el dominio**

Run: `dotnet build "backend/src/Calorias.Domain/Calorias.Domain.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git -C backend add src/Calorias.Domain/Entities/Usuario.cs
git -C backend commit -m "feat(domain): campos de perfil fisico y split de macros en Usuario"
```

---

### Task 3: `CalculadoraObjetivos` (servicio puro de dominio) — TDD

**Files:**
- Create: `backend/src/Calorias.Domain/Servicios/CalculadoraObjetivos.cs`
- Create: `backend/tests/Calorias.Tests/CalculadoraObjetivosTests.cs`

- [ ] **Step 1: Escribir los tests que fallan**

Crear `backend/tests/Calorias.Tests/CalculadoraObjetivosTests.cs`:

```csharp
using Calorias.Domain.Entities;
using Calorias.Domain.Servicios;
using Xunit;

namespace Calorias.Tests;

public class CalculadoraObjetivosTests
{
    // Hombre, mantener, sedentario: TMB = 10*80 + 6.25*180 - 5*30 + 5 = 1780;
    // TDEE = 1780 * 1.2 = 2136; sin ajuste; por encima del piso => meta 2136.
    [Fact]
    public void Hombre_mantener_sedentario_sin_override()
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Masculino, edad: 30, alturaCm: 180, pesoKg: 80m,
            NivelActividad.Sedentario, Objetivo.Mantener, ritmoKgSemana: 0m,
            metaCaloriasOverride: null, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.Equal(1780, r.Tmb);
        Assert.Equal(2136, r.Tdee);
        Assert.Equal(2136, r.MetaCalorias);
        Assert.False(r.MetaTopada);
        // Macros: prot 30%*2136/4=160.2->160; carbs 40%*2136/4=213.6->214; grasas 30%*2136/9=71.2->71
        Assert.Equal(160, r.ProteinaG);
        Assert.Equal(214, r.CarbosG);
        Assert.Equal(71, r.GrasasG);
    }

    // Mujer, perder, moderado: TMB = 10*60 + 6.25*165 - 5*28 - 161 = 1330.25;
    // TDEE = 1330.25*1.55 = 2061.8875; ajuste = 0.5*7700/7 = 550; perder => 1511.8875;
    // piso = max(1330.25, 1200) = 1330.25 (no topa); meta = round(1511.8875) = 1512.
    [Fact]
    public void Mujer_perder_moderado_sin_override()
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Femenino, edad: 28, alturaCm: 165, pesoKg: 60m,
            NivelActividad.Moderado, Objetivo.Perder, ritmoKgSemana: 0.5m,
            metaCaloriasOverride: null, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.Equal(1330, r.Tmb);
        Assert.Equal(2062, r.Tdee);
        Assert.Equal(1512, r.MetaCalorias);
        Assert.False(r.MetaTopada);
    }

    // Mujer agresiva: TMB = 550+1000-125-161 = 1264; TDEE = 1264*1.2 = 1516.8;
    // ajuste = 1.0*7700/7 = 1100; perder => 416.8; piso = max(1264,1200)=1264;
    // 416.8 < 1264 => topa, meta = 1264.
    [Fact]
    public void Deficit_agresivo_se_topa_en_el_piso_de_seguridad()
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Femenino, edad: 25, alturaCm: 160, pesoKg: 55m,
            NivelActividad.Sedentario, Objetivo.Perder, ritmoKgSemana: 1.0m,
            metaCaloriasOverride: null, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.True(r.MetaTopada);
        Assert.Equal(1264, r.MetaCalorias);
    }

    // Override manda: cálculo daría 2136, pero override 1900 => meta 1900 y no topada.
    [Fact]
    public void Override_manda_sobre_el_calculo()
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Masculino, edad: 30, alturaCm: 180, pesoKg: 80m,
            NivelActividad.Sedentario, Objetivo.Mantener, ritmoKgSemana: 0m,
            metaCaloriasOverride: 1900, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.Equal(1900, r.MetaCalorias);
        Assert.False(r.MetaTopada);
        // Macros desde 1900: prot 30%*1900/4=142.5->143 (AwayFromZero); carbs 190; grasas 63.33->63
        Assert.Equal(143, r.ProteinaG);
        Assert.Equal(190, r.CarbosG);
        Assert.Equal(63, r.GrasasG);
    }

    [Theory]
    [InlineData(NivelActividad.Sedentario, 2136)]
    [InlineData(NivelActividad.Ligero, 2448)]    // 1780*1.375 = 2447.5 -> 2448
    [InlineData(NivelActividad.Moderado, 2759)]  // 1780*1.55  = 2759
    [InlineData(NivelActividad.Activo, 3071)]    // 1780*1.725 = 3070.5 -> 3071
    [InlineData(NivelActividad.MuyActivo, 3382)] // 1780*1.9   = 3382
    public void Factor_de_actividad_se_aplica(NivelActividad nivel, int metaEsperada)
    {
        var r = CalculadoraObjetivos.Calcular(
            Sexo.Masculino, edad: 30, alturaCm: 180, pesoKg: 80m,
            nivel, Objetivo.Mantener, ritmoKgSemana: 0m,
            metaCaloriasOverride: null, proteinaPct: 30, carbosPct: 40, grasasPct: 30);

        Assert.Equal(metaEsperada, r.MetaCalorias);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que NO compila (falla)**

Run: `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"`
Expected: FALLA de compilación — `CalculadoraObjetivos` no existe todavía.

- [ ] **Step 3: Implementar la calculadora**

Crear `backend/src/Calorias.Domain/Servicios/CalculadoraObjetivos.cs`:

```csharp
using Calorias.Domain.Entities;

namespace Calorias.Domain.Servicios;

/// <summary>Resultado del cálculo de objetivos: meta calórica, banderas y macros en gramos.</summary>
public record ResultadoObjetivos(
    int MetaCalorias,
    bool MetaTopada,
    int ProteinaG,
    int CarbosG,
    int GrasasG,
    int Tmb,
    int Tdee);

/// <summary>
/// Cálculo de objetivos calóricos (Mifflin-St Jeor → TDEE → ajuste por objetivo → piso de
/// seguridad → override). Servicio puro: sin estado ni dependencias, totalmente unit-testeable.
/// </summary>
public static class CalculadoraObjetivos
{
    private const decimal KcalPorKgGrasa = 7700m;

    public static decimal FactorActividad(NivelActividad nivel) => nivel switch
    {
        NivelActividad.Sedentario => 1.2m,
        NivelActividad.Ligero     => 1.375m,
        NivelActividad.Moderado   => 1.55m,
        NivelActividad.Activo     => 1.725m,
        NivelActividad.MuyActivo  => 1.9m,
        _                         => 1.2m,
    };

    public static ResultadoObjetivos Calcular(
        Sexo sexo, int edad, int alturaCm, decimal pesoKg,
        NivelActividad nivel, Objetivo objetivo, decimal ritmoKgSemana,
        int? metaCaloriasOverride, int proteinaPct, int carbosPct, int grasasPct)
    {
        // TMB (Mifflin-St Jeor)
        decimal tmb = 10m * pesoKg + 6.25m * alturaCm - 5m * edad
                      + (sexo == Sexo.Masculino ? 5m : -161m);

        decimal tdee = tmb * FactorActividad(nivel);

        // Ajuste por objetivo: 1 kg grasa ≈ 7700 kcal; ajuste diario = ritmo*7700/7.
        decimal ajuste = ritmoKgSemana * KcalPorKgGrasa / 7m;
        decimal objetivoCal = objetivo switch
        {
            Objetivo.Perder => tdee - ajuste,
            Objetivo.Ganar  => tdee + ajuste,
            _               => tdee, // Mantener
        };

        // Piso de seguridad: nunca por debajo de la TMB ni del mínimo por sexo.
        decimal piso = Math.Max(tmb, sexo == Sexo.Masculino ? 1500m : 1200m);
        bool topada = metaCaloriasOverride is null && objetivoCal < piso;
        decimal calculada = Math.Max(objetivoCal, piso);

        int metaFinal = metaCaloriasOverride
                        ?? (int)Math.Round(calculada, MidpointRounding.AwayFromZero);

        int Gramos(int pct, int kcalPorGramo) =>
            (int)Math.Round(pct / 100m * metaFinal / kcalPorGramo, MidpointRounding.AwayFromZero);

        return new ResultadoObjetivos(
            MetaCalorias: metaFinal,
            MetaTopada: topada,
            ProteinaG: Gramos(proteinaPct, 4),
            CarbosG:   Gramos(carbosPct, 4),
            GrasasG:   Gramos(grasasPct, 9),
            Tmb:  (int)Math.Round(tmb, MidpointRounding.AwayFromZero),
            Tdee: (int)Math.Round(tdee, MidpointRounding.AwayFromZero));
    }
}
```

- [ ] **Step 4: Ejecutar los tests y verificar que pasan**

Run: `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"`
Expected: PASS (los 4 de Fase A + los nuevos de `CalculadoraObjetivos`: 4 `[Fact]` + 5 casos `[Theory]`).

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Domain/Servicios/CalculadoraObjetivos.cs tests/Calorias.Tests/CalculadoraObjetivosTests.cs
git -C backend commit -m "feat(domain): CalculadoraObjetivos (TDEE Mifflin-St Jeor) + tests"
```

---

### Task 4: Mapping EF de los campos de perfil

**Files:**
- Modify: `backend/src/Calorias.Infrastructure/Persistence/CaloriasDbContext.cs`

- [ ] **Step 1: Configurar los campos de perfil en el `Entity<Usuario>`**

En `CaloriasDbContext.cs`, dentro del bloque `b.Entity<Usuario>(e => { ... })`, justo después
de `e.HasIndex(x => x.Email);` añadir:

```csharp

            // --- Perfil físico ---
            // Enums nullables como texto legible en la BD.
            e.Property(x => x.Sexo).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.NivelActividad).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Objetivo).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.PesoKg).HasPrecision(5, 2);
            e.Property(x => x.RitmoKgSemana).HasPrecision(4, 2);
            // Split de macros: NOT NULL con default (las filas existentes quedan en 30/40/30).
            e.Property(x => x.MetaProteinaPct).HasDefaultValue(30);
            e.Property(x => x.MetaCarbosPct).HasDefaultValue(40);
            e.Property(x => x.MetaGrasasPct).HasDefaultValue(30);
```

- [ ] **Step 2: Compilar Infrastructure**

Run: `dotnet build "backend/src/Calorias.Infrastructure/Calorias.Infrastructure.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git -C backend add src/Calorias.Infrastructure/Persistence/CaloriasDbContext.cs
git -C backend commit -m "feat(infra): mapping EF de los campos de perfil en Usuario"
```

---

### Task 5: Migración `PerfilUsuario`

**Files:**
- Create: `backend/src/Calorias.Infrastructure/Migrations/*_PerfilUsuario.cs` (generado)

> **Importante:** parar el backend antes de migrar (bloquea las DLLs).

- [ ] **Step 1: Generar la migración**

Run (PowerShell, desde la carpeta raíz del workspace `App Calorias`):

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet ef migrations add PerfilUsuario `
  --project "backend/src/Calorias.Infrastructure" `
  --startup-project "backend/src/Calorias.Api"
```

Expected: `Done.` y archivos nuevos en `src/Calorias.Infrastructure/Migrations/`.

- [ ] **Step 2: Revisar la migración generada**

Abrir el `*_PerfilUsuario.cs` y verificar que en `Up()`:
- añade a `Usuarios` las columnas `Sexo` (text, null), `NivelActividad` (text, null),
  `Objetivo` (text, null), `FechaNacimiento` (date, null), `AlturaCm` (int, null),
  `PesoKg` (numeric(5,2), null), `RitmoKgSemana` (numeric(4,2), null),
  `MetaCaloriasOverride` (int, null),
- añade `MetaProteinaPct`/`MetaCarbosPct`/`MetaGrasasPct` (int, NOT NULL, defaultValue 30/40/30).

Si algo no aparece, revisar Task 4 y regenerar (`dotnet ef migrations remove --project backend/src/Calorias.Infrastructure --startup-project backend/src/Calorias.Api` y repetir Step 1).

- [ ] **Step 3: Aplicar la migración a la BD de desarrollo**

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet ef database update `
  --project "backend/src/Calorias.Infrastructure" `
  --startup-project "backend/src/Calorias.Api"
```

Expected: `Done.` sin errores.

- [ ] **Step 4: Commit**

```bash
git -C backend add src/Calorias.Infrastructure/Migrations
git -C backend commit -m "feat(infra): migracion PerfilUsuario"
```

---

### Task 6: DTOs y `PerfilController` (GET/PUT `/api/perfil`)

**Files:**
- Create: `backend/src/Calorias.Api/Dtos/PerfilDto.cs`
- Create: `backend/src/Calorias.Api/Controllers/PerfilController.cs`

- [ ] **Step 1: Crear los DTOs**

Crear `backend/src/Calorias.Api/Dtos/PerfilDto.cs`:

```csharp
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
```

- [ ] **Step 2: Crear el controller**

Crear `backend/src/Calorias.Api/Controllers/PerfilController.cs`:

```csharp
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
```

- [ ] **Step 3: Compilar la API**

Run: `dotnet build "backend/src/Calorias.Api/Calorias.Api.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 4: Verificación manual (con backend corriendo)**

Arrancar el backend (parar antes cualquier instancia; `$env:ASPNETCORE_ENVIRONMENT='Development'`,
`$env:GOOGLE_APPLICATION_CREDENTIALS='C:\Users\LENOVO\secrets\calorias-498417-77abb89d3162.json'`,
`dotnet run --project backend/src/Calorias.Api --launch-profile http`). Sin token, `GET
http://localhost:5247/api/perfil` debe devolver **401** (auth activa). La verificación con token
real se hace desde la pantalla Perfil en Task 9.

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Api/Dtos/PerfilDto.cs src/Calorias.Api/Controllers/PerfilController.cs
git -C backend commit -m "feat(api): GET/PUT /api/perfil con meta calculada"
```

---

### Task 7: Cliente de API del perfil (frontend)

**Files:**
- Modify: `frontend/src/config.ts`
- Create: `frontend/src/api/perfil.ts`

- [ ] **Step 1: Añadir `PERFIL_URL` a la config**

En `frontend/src/config.ts`, después de la línea que define `ANALIZAR_URL`, añadir:

```typescript

/** Endpoint del perfil del usuario (GET/PUT). Requiere Bearer idToken. */
export const PERFIL_URL = `${API_BASE_URL}/api/perfil`;
```

- [ ] **Step 2: Crear el cliente `api/perfil.ts`**

Crear `frontend/src/api/perfil.ts`:

```typescript
import { PERFIL_URL } from '../config';
import { ApiError } from './comidas';

export interface Macros {
  proteinaG: number;
  carbosG: number;
  grasasG: number;
}

/** Forma de la respuesta de GET/PUT /api/perfil (camelCase del backend). */
export interface Perfil {
  perfilCompleto: boolean;
  sexo: string | null;
  fechaNacimiento: string | null;
  alturaCm: number | null;
  pesoKg: number | null;
  nivelActividad: string | null;
  objetivo: string | null;
  ritmoKgSemana: number | null;
  metaProteinaPct: number;
  metaCarbosPct: number;
  metaGrasasPct: number;
  metaCaloriasOverride: number | null;
  metaCalorias: number | null;
  metaTopada: boolean;
  macros: Macros | null;
  tmb: number | null;
  tdee: number | null;
}

/** Body de PUT (sin los campos calculados). */
export interface PerfilInput {
  sexo: string | null;
  fechaNacimiento: string | null;
  alturaCm: number | null;
  pesoKg: number | null;
  nivelActividad: string | null;
  objetivo: string | null;
  ritmoKgSemana: number | null;
  metaProteinaPct: number;
  metaCarbosPct: number;
  metaGrasasPct: number;
  metaCaloriasOverride: number | null;
}

async function leerRespuesta(res: Response): Promise<Perfil> {
  if (!res.ok) {
    const detalle = await res.text().catch(() => '');
    if (res.status === 401) {
      throw new ApiError('No autorizado (401): el token de Google no es válido o expiró.', 401);
    }
    throw new ApiError(`El backend respondió ${res.status}. ${detalle.slice(0, 300)}`, res.status);
  }
  return (await res.json()) as Perfil;
}

export async function getPerfil(idToken: string): Promise<Perfil> {
  let res: Response;
  try {
    res = await fetch(PERFIL_URL, {
      method: 'GET',
      headers: { Authorization: `Bearer ${idToken}`, Accept: 'application/json' },
    });
  } catch (e) {
    throw new ApiError(
      `No se pudo conectar con el backend (${PERFIL_URL}). ${e instanceof Error ? e.message : ''}`,
      0,
    );
  }
  return leerRespuesta(res);
}

export async function putPerfil(idToken: string, input: PerfilInput): Promise<Perfil> {
  let res: Response;
  try {
    res = await fetch(PERFIL_URL, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${idToken}`,
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(input),
    });
  } catch (e) {
    throw new ApiError(
      `No se pudo conectar con el backend (${PERFIL_URL}). ${e instanceof Error ? e.message : ''}`,
      0,
    );
  }
  return leerRespuesta(res);
}
```

- [ ] **Step 3: Verificar tipos**

Run: `npx tsc --noEmit --project frontend/tsconfig.json`
Expected: sin errores.

- [ ] **Step 4: Commit**

```bash
git -C frontend add src/config.ts src/api/perfil.ts
git -C frontend commit -m "feat(api): cliente getPerfil/putPerfil + PERFIL_URL"
```

---

### Task 8: Pantalla `PerfilScreen` (frontend)

**Files:**
- Create: `frontend/src/screens/PerfilScreen.tsx`

> **Sub-skill UI:** al implementar esta pantalla, seguir `frontend-design:frontend-design` para
> el acabado visual (jerarquía, espaciado, estados). El código de abajo es funcional y completo;
> mejora la estética sin cambiar el contrato de datos (campos enviados a `putPerfil`).

- [ ] **Step 1: Crear `PerfilScreen.tsx`**

Crear `frontend/src/screens/PerfilScreen.tsx`:

```tsx
import { useEffect, useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import type { GoogleAuthState } from '../auth/useGoogleAuth';
import { ApiError } from '../api/comidas';
import { getPerfil, putPerfil, type Perfil, type PerfilInput } from '../api/perfil';
import { colors, radius, spacing } from '../theme';

const SEXOS = ['Masculino', 'Femenino'] as const;
const NIVELES = ['Sedentario', 'Ligero', 'Moderado', 'Activo', 'MuyActivo'] as const;
const OBJETIVOS = ['Perder', 'Mantener', 'Ganar'] as const;

/** Convierte texto de un input numérico a number|null (vacío => null). */
function num(s: string): number | null {
  const t = s.trim().replace(',', '.');
  if (t === '') return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}

export default function PerfilScreen({
  auth,
  onClose,
}: {
  auth: GoogleAuthState;
  onClose: () => void;
}) {
  const [cargando, setCargando] = useState(true);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [perfil, setPerfil] = useState<Perfil | null>(null);

  // Campos editables (string para los inputs numéricos).
  const [sexo, setSexo] = useState<string | null>(null);
  const [fechaNacimiento, setFechaNacimiento] = useState('');
  const [alturaCm, setAlturaCm] = useState('');
  const [pesoKg, setPesoKg] = useState('');
  const [nivelActividad, setNivelActividad] = useState<string | null>(null);
  const [objetivo, setObjetivo] = useState<string | null>(null);
  const [ritmoKgSemana, setRitmoKgSemana] = useState('');
  const [protPct, setProtPct] = useState('30');
  const [carbPct, setCarbPct] = useState('40');
  const [grasaPct, setGrasaPct] = useState('30');
  const [override, setOverride] = useState('');

  function aplicar(p: Perfil) {
    setPerfil(p);
    setSexo(p.sexo);
    setFechaNacimiento(p.fechaNacimiento ?? '');
    setAlturaCm(p.alturaCm?.toString() ?? '');
    setPesoKg(p.pesoKg?.toString() ?? '');
    setNivelActividad(p.nivelActividad);
    setObjetivo(p.objetivo);
    setRitmoKgSemana(p.ritmoKgSemana?.toString() ?? '');
    setProtPct(p.metaProteinaPct.toString());
    setCarbPct(p.metaCarbosPct.toString());
    setGrasaPct(p.metaGrasasPct.toString());
    setOverride(p.metaCaloriasOverride?.toString() ?? '');
  }

  useEffect(() => {
    let activo = true;
    if (!auth.idToken) {
      setCargando(false);
      setError('Inicia sesión para ver tu perfil.');
      return;
    }
    setCargando(true);
    getPerfil(auth.idToken)
      .then((p) => {
        if (activo) {
          aplicar(p);
          setError(null);
        }
      })
      .catch((e) => {
        if (activo) setError(e instanceof ApiError ? e.message : 'No se pudo cargar el perfil.');
      })
      .finally(() => {
        if (activo) setCargando(false);
      });
    return () => {
      activo = false;
    };
  }, [auth.idToken]);

  const pctSuma = useMemo(
    () => (num(protPct) ?? 0) + (num(carbPct) ?? 0) + (num(grasaPct) ?? 0),
    [protPct, carbPct, grasaPct],
  );

  async function guardar() {
    if (!auth.idToken) return;
    const input: PerfilInput = {
      sexo,
      fechaNacimiento: fechaNacimiento.trim() === '' ? null : fechaNacimiento.trim(),
      alturaCm: num(alturaCm),
      pesoKg: num(pesoKg),
      nivelActividad,
      objetivo,
      ritmoKgSemana: num(ritmoKgSemana),
      metaProteinaPct: num(protPct) ?? 30,
      metaCarbosPct: num(carbPct) ?? 40,
      metaGrasasPct: num(grasaPct) ?? 30,
      metaCaloriasOverride: num(override),
    };
    setGuardando(true);
    setError(null);
    try {
      const p = await putPerfil(auth.idToken, input);
      aplicar(p);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'No se pudo guardar el perfil.');
    } finally {
      setGuardando(false);
    }
  }

  if (cargando) {
    return (
      <View style={styles.centro}>
        <ActivityIndicator color={colors.primary} />
      </View>
    );
  }

  return (
    <ScrollView style={styles.root} contentContainerStyle={styles.content}>
      <View style={styles.topRow}>
        <Pressable onPress={onClose} hitSlop={10}>
          <Text style={styles.back}>‹ Volver</Text>
        </Pressable>
        <Text style={styles.title}>Perfil</Text>
        <View style={{ width: 60 }} />
      </View>

      {perfil && !perfil.perfilCompleto && (
        <View style={styles.aviso}>
          <Text style={styles.avisoText}>
            Completa todos los datos para calcular tu meta de calorías.
          </Text>
        </View>
      )}

      {error && <Text style={styles.error}>{error}</Text>}

      {/* Meta calculada */}
      {perfil?.perfilCompleto && perfil.metaCalorias != null && (
        <View style={styles.metaCard}>
          <Text style={styles.metaKcal}>{perfil.metaCalorias} kcal/día</Text>
          <Text style={styles.metaSub}>
            TMB {perfil.tmb} · TDEE {perfil.tdee}
            {perfil.metaTopada ? ' · topada al mínimo seguro' : ''}
          </Text>
          {perfil.macros && (
            <Text style={styles.metaSub}>
              P {perfil.macros.proteinaG} g · C {perfil.macros.carbosG} g · G{' '}
              {perfil.macros.grasasG} g
            </Text>
          )}
        </View>
      )}

      <Campo label="Sexo">
        <Chips options={SEXOS} value={sexo} onChange={setSexo} />
      </Campo>

      <Campo label="Fecha de nacimiento (YYYY-MM-DD)">
        <Input value={fechaNacimiento} onChangeText={setFechaNacimiento} placeholder="1995-04-10" />
      </Campo>

      <Campo label="Altura (cm)">
        <Input value={alturaCm} onChangeText={setAlturaCm} keyboardType="numeric" placeholder="178" />
      </Campo>

      <Campo label="Peso (kg)">
        <Input value={pesoKg} onChangeText={setPesoKg} keyboardType="numeric" placeholder="80" />
      </Campo>

      <Campo label="Nivel de actividad">
        <Chips options={NIVELES} value={nivelActividad} onChange={setNivelActividad} />
      </Campo>

      <Campo label="Objetivo">
        <Chips options={OBJETIVOS} value={objetivo} onChange={setObjetivo} />
      </Campo>

      <Campo label="Ritmo (kg/semana)">
        <Input
          value={ritmoKgSemana}
          onChangeText={setRitmoKgSemana}
          keyboardType="numeric"
          placeholder="0.5"
        />
      </Campo>

      <Campo label={`Split de macros (% — suman ${pctSuma})`}>
        <View style={styles.pctRow}>
          <Input value={protPct} onChangeText={setProtPct} keyboardType="numeric" style={styles.pctInput} />
          <Input value={carbPct} onChangeText={setCarbPct} keyboardType="numeric" style={styles.pctInput} />
          <Input value={grasaPct} onChangeText={setGrasaPct} keyboardType="numeric" style={styles.pctInput} />
        </View>
        <Text style={styles.hint}>Proteína · Carbos · Grasas</Text>
      </Campo>

      <Campo label="Override de calorías (opcional)">
        <Input value={override} onChangeText={setOverride} keyboardType="numeric" placeholder="manual" />
      </Campo>

      <Pressable
        onPress={guardar}
        disabled={guardando}
        style={({ pressed }) => [styles.boton, pressed && styles.pressed, guardando && styles.botonOff]}
      >
        <Text style={styles.botonText}>{guardando ? 'Guardando…' : 'Guardar perfil'}</Text>
      </Pressable>
    </ScrollView>
  );
}

function Campo({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <View style={styles.campo}>
      <Text style={styles.label}>{label}</Text>
      {children}
    </View>
  );
}

function Input({ style, ...props }: React.ComponentProps<typeof TextInput>) {
  return (
    <TextInput
      placeholderTextColor={colors.textMuted}
      style={[styles.input, style]}
      {...props}
    />
  );
}

function Chips({
  options,
  value,
  onChange,
}: {
  options: readonly string[];
  value: string | null;
  onChange: (v: string) => void;
}) {
  return (
    <View style={styles.chipRow}>
      {options.map((o) => (
        <Pressable
          key={o}
          onPress={() => onChange(o)}
          style={({ pressed }) => [
            styles.chip,
            value === o && styles.chipActive,
            pressed && styles.pressed,
          ]}
        >
          <Text style={[styles.chipText, value === o && styles.chipTextActive]}>{o}</Text>
        </Pressable>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: colors.bg },
  content: { padding: spacing.md, gap: spacing.sm, paddingBottom: spacing.xl },
  centro: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.bg },
  topRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  back: { color: colors.accent, fontSize: 16, fontWeight: '600', width: 60 },
  title: { color: colors.text, fontSize: 22, fontWeight: '800' },
  aviso: {
    backgroundColor: colors.surfaceAlt,
    borderColor: colors.warning,
    borderWidth: 1,
    borderRadius: radius.md,
    padding: spacing.sm,
  },
  avisoText: { color: colors.warning, fontSize: 13 },
  error: { color: colors.danger, fontSize: 13 },
  metaCard: {
    backgroundColor: colors.surface,
    borderColor: colors.border,
    borderWidth: 1,
    borderRadius: radius.lg,
    padding: spacing.md,
    gap: 4,
  },
  metaKcal: { color: colors.primary, fontSize: 28, fontWeight: '800' },
  metaSub: { color: colors.textMuted, fontSize: 13 },
  campo: { gap: spacing.xs },
  label: { color: colors.text, fontSize: 14, fontWeight: '600' },
  hint: { color: colors.textMuted, fontSize: 12 },
  input: {
    backgroundColor: colors.surface,
    borderColor: colors.border,
    borderWidth: 1,
    borderRadius: radius.md,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    color: colors.text,
    fontSize: 15,
  },
  pctRow: { flexDirection: 'row', gap: spacing.sm },
  pctInput: { flex: 1, textAlign: 'center' },
  chipRow: { flexDirection: 'row', gap: spacing.xs, flexWrap: 'wrap' },
  chip: {
    paddingVertical: spacing.xs,
    paddingHorizontal: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.surfaceAlt,
    borderWidth: 1,
    borderColor: colors.border,
  },
  chipActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  chipText: { color: colors.textMuted, fontSize: 13, fontWeight: '600' },
  chipTextActive: { color: colors.bg },
  boton: {
    marginTop: spacing.sm,
    backgroundColor: colors.primary,
    borderRadius: radius.md,
    paddingVertical: spacing.md,
    alignItems: 'center',
  },
  botonOff: { backgroundColor: colors.primaryDark },
  botonText: { color: colors.bg, fontSize: 16, fontWeight: '700' },
  pressed: { opacity: 0.7 },
});
```

- [ ] **Step 2: Verificar tipos**

Run: `npx tsc --noEmit --project frontend/tsconfig.json`
Expected: sin errores.

- [ ] **Step 3: Commit**

```bash
git -C frontend add src/screens/PerfilScreen.tsx
git -C frontend commit -m "feat(perfil): pantalla de perfil fisico con meta calculada"
```

---

### Task 9: Avatar de perfil en la cabecera + navegación (`App.tsx`)

**Files:**
- Modify: `frontend/App.tsx`

- [ ] **Step 1: Actualizar imports**

En `frontend/App.tsx`, reemplazar la línea de imports de `react-native`:

```typescript
import { Pressable, StyleSheet, Text, View } from 'react-native';
```

por:

```typescript
import { Image, Pressable, StyleSheet, Text, View } from 'react-native';
```

Y debajo de `import HistoryScreen from './src/screens/HistoryScreen';` añadir:

```typescript
import PerfilScreen from './src/screens/PerfilScreen';
```

- [ ] **Step 2: Añadir el estado de pantalla y la cabecera con avatar**

Reemplazar el cuerpo del componente `App` (desde `const [tab, setTab] = useState<Tab>('captura');`
hasta el cierre del `return`) por:

```tsx
  const [tab, setTab] = useState<Tab>('captura');
  const [screen, setScreen] = useState<'main' | 'perfil'>('main');

  const inicial = (auth.user?.name ?? auth.user?.email ?? '?').slice(0, 1).toUpperCase();

  return (
    <GestureHandlerRootView style={styles.flex}>
      <SafeAreaProvider>
        <SafeAreaView style={styles.root} edges={['top', 'bottom']}>
          <StatusBar style="light" />

          {screen === 'perfil' ? (
            <PerfilScreen auth={auth} onClose={() => setScreen('main')} />
          ) : (
            <>
              <View style={styles.header}>
                <Text style={styles.brand}>Calorías</Text>
                <Pressable
                  onPress={() => setScreen('perfil')}
                  style={({ pressed }) => [styles.avatar, pressed && styles.pressed]}
                  accessibilityLabel="Abrir perfil"
                >
                  {auth.user?.picture ? (
                    <Image source={{ uri: auth.user.picture }} style={styles.avatarImg} />
                  ) : (
                    <Text style={styles.avatarText}>{inicial}</Text>
                  )}
                </Pressable>
              </View>

              <View style={styles.body}>
                {tab === 'captura' ? (
                  <CaptureScreen auth={auth} />
                ) : (
                  <HistoryScreen auth={auth} />
                )}
              </View>

              <View style={styles.tabBar}>
                <TabButton
                  label="Captura"
                  active={tab === 'captura'}
                  onPress={() => setTab('captura')}
                />
                <TabButton
                  label="Historial"
                  active={tab === 'historial'}
                  onPress={() => setTab('historial')}
                />
              </View>
            </>
          )}
        </SafeAreaView>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
```

- [ ] **Step 3: Añadir los estilos de la cabecera y el avatar**

Dentro de `StyleSheet.create({ ... })`, después de la entrada `body: { flex: 1 },`, añadir:

```typescript
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderBottomWidth: 1,
    borderBottomColor: colors.border,
    backgroundColor: colors.surface,
  },
  brand: { color: colors.text, fontSize: 18, fontWeight: '800' },
  avatar: {
    width: 36,
    height: 36,
    borderRadius: 18,
    backgroundColor: colors.surfaceAlt,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
  },
  avatarImg: { width: '100%', height: '100%' },
  avatarText: { color: colors.primary, fontWeight: '800', fontSize: 16 },
```

- [ ] **Step 4: Verificar tipos**

Run: `npx tsc --noEmit --project frontend/tsconfig.json`
Expected: sin errores.

- [ ] **Step 5: Verificación manual por web**

Arrancar backend (con `GOOGLE_APPLICATION_CREDENTIALS`) y frontend (`npm run web`). Login →
tocar el avatar (arriba derecha) → se abre Perfil. Rellenar sexo, fecha nac., altura, peso,
actividad, objetivo, ritmo → **Guardar** → la tarjeta de meta muestra kcal/día + macros.
Verificar en la BD que la fila de `Usuarios` guardó los campos de perfil:

```powershell
$env:PGPASSWORD='1234'
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -d calorias -c 'SELECT "Sexo","FechaNacimiento","AlturaCm","PesoKg","NivelActividad","Objetivo","RitmoKgSemana","MetaCaloriasOverride","MetaProteinaPct" FROM "Usuarios";'
```

Cerrar sesión y volver a entrar: el perfil debe **persistir** (el upsert no lo pisa).

- [ ] **Step 6: Commit**

```bash
git -C frontend add App.tsx
git -C frontend commit -m "feat(nav): avatar de perfil en cabecera abre pantalla Perfil"
```

---

## Verificación final de la Fase B

- [ ] `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"` → PASS (Fase A + CalculadoraObjetivos).
- [ ] `npx tsc --noEmit --project frontend/tsconfig.json` → sin errores.
- [ ] `GET /api/perfil` sin token → 401; con token y perfil incompleto → `perfilCompleto:false`,
  `metaCalorias:null`.
- [ ] Flujo manual: completar el perfil en la pantalla → `PUT /api/perfil` devuelve la meta
  calculada (kcal + macros) y la BD persiste los campos. Re-login conserva el perfil.
- [ ] Caso piso de seguridad: objetivo Perder con ritmo alto → `metaTopada:true` y meta no baja
  del mínimo por sexo.

## Notas para Fase C (no implementar aquí)

- `GET /api/comidas/resumen` usará `CalculadoraObjetivos` (vía perfil) para la línea de meta y
  `ResumenDiario` (rollup de Fase A) para los promedios por período.
- La navegación añadirá un tercer tab **Dashboard** junto a Captura/Historial.
