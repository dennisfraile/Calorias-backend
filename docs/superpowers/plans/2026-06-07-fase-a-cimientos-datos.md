# Fase A — Cimientos de datos (TipoComida + FechaLocal + rollup ResumenDiario) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que cada comida se etiquete por tipo (desayuno/almuerzo/cena/snack) y día local, y que un rollup diario `ResumenDiario` se mantenga sincronizado al guardar comidas.

**Architecture:** .NET 10 Clean Architecture. Se añaden campos al dominio (`RegistroComida`) y una entidad de rollup (`ResumenDiario`). Un servicio `ServicioResumenDiario` recalcula el total de un día desde los registros reales (estrategia *recompute-the-day*, idempotente). El controller de análisis fija tipo/fecha y dispara el rollup dentro de una transacción. El frontend añade un selector de tipo de comida.

**Tech Stack:** C# / EF Core 8+ (Npgsql/PostgreSQL), xUnit + EF Core InMemory para tests, Expo/React Native (TypeScript) en el frontend.

**Spec:** `docs/superpowers/specs/2026-06-07-comidas-objetivos-dashboards-design.md`

**Rutas base:**
- Backend: `c:\Users\LENOVO\Documents\Portafolio\App Calorias\backend`
- Frontend: `c:\Users\LENOVO\Documents\Portafolio\App Calorias\frontend`

> Nota EF: los comandos `dotnet ef` requieren `ASPNETCORE_ENVIRONMENT=Development` (la cadena de conexión vive en user-secrets) y el tool `dotnet-ef`. Si no está: `dotnet tool install --global dotnet-ef`.

---

### Task 1: Enum `TipoComida` y campos nuevos en `RegistroComida`

**Files:**
- Create: `backend/src/Calorias.Domain/Entities/TipoComida.cs`
- Modify: `backend/src/Calorias.Domain/Entities/RegistroComida.cs`

- [ ] **Step 1: Crear el enum**

Crear `backend/src/Calorias.Domain/Entities/TipoComida.cs`:

```csharp
namespace Calorias.Domain.Entities;

public enum TipoComida
{
    Desayuno,
    Almuerzo,
    Cena,
    Snack
}
```

- [ ] **Step 2: Añadir campos a `RegistroComida`**

En `backend/src/Calorias.Domain/Entities/RegistroComida.cs`, justo debajo de la línea
`public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;` añadir:

```csharp
    /// <summary>Día local del usuario (lo envía el cliente). Clave de agrupación diaria.</summary>
    public DateOnly FechaLocal { get; set; }

    /// <summary>Tipo de comida (desayuno/almuerzo/cena/snack).</summary>
    public TipoComida Tipo { get; set; }
```

- [ ] **Step 3: Compilar el proyecto de dominio**

Run: `dotnet build "backend/src/Calorias.Domain/Calorias.Domain.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git -C backend add src/Calorias.Domain/Entities/TipoComida.cs src/Calorias.Domain/Entities/RegistroComida.cs
git -C backend commit -m "feat(domain): TipoComida y FechaLocal en RegistroComida"
```

---

### Task 2: Entidad de rollup `ResumenDiario`

**Files:**
- Create: `backend/src/Calorias.Domain/Entities/ResumenDiario.cs`

- [ ] **Step 1: Crear la entidad**

Crear `backend/src/Calorias.Domain/Entities/ResumenDiario.cs`:

```csharp
namespace Calorias.Domain.Entities;

/// <summary>
/// Rollup pre-agregado por usuario y día local. Es la única granularidad mantenida
/// incrementalmente; los períodos más gruesos se calculan con GROUP BY sobre esta tabla.
/// </summary>
public class ResumenDiario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UsuarioId { get; set; } = default!;
    public DateOnly FechaLocal { get; set; }

    public decimal CaloriasTotal { get; set; }
    public decimal ProteinasTotal { get; set; }
    public decimal CarbosTotal { get; set; }
    public decimal GrasasTotal { get; set; }
    public int NumComidas { get; set; }
}
```

- [ ] **Step 2: Compilar**

Run: `dotnet build "backend/src/Calorias.Domain/Calorias.Domain.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git -C backend add src/Calorias.Domain/Entities/ResumenDiario.cs
git -C backend commit -m "feat(domain): entidad de rollup ResumenDiario"
```

---

### Task 3: Configuración EF (DbSet, columnas, índices)

**Files:**
- Modify: `backend/src/Calorias.Infrastructure/Persistence/CaloriasDbContext.cs`

- [ ] **Step 1: Añadir el DbSet**

En `CaloriasDbContext.cs`, debajo de
`public DbSet<DetalleComida> Detalles => Set<DetalleComida>();` añadir:

```csharp
    public DbSet<ResumenDiario> ResumenesDiarios => Set<ResumenDiario>();
```

- [ ] **Step 2: Configurar `RegistroComida.Tipo` y un índice por (UsuarioId, FechaLocal)**

Dentro del bloque `b.Entity<RegistroComida>(e => { ... })`, antes del `e.HasOne(x => x.Usuario)`, añadir:

```csharp
            // Enum como texto legible en la BD.
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            // Acelera el recompute-the-day y las consultas por día.
            e.HasIndex(x => new { x.UsuarioId, x.FechaLocal });
```

- [ ] **Step 3: Configurar la entidad `ResumenDiario`**

Después del bloque `b.Entity<DetalleComida>(e => { ... });` (al final de `OnModelCreating`), añadir:

```csharp
        b.Entity<ResumenDiario>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UsuarioId, x.FechaLocal }).IsUnique();
            e.Property(x => x.CaloriasTotal).HasPrecision(10, 2);
            e.Property(x => x.ProteinasTotal).HasPrecision(10, 2);
            e.Property(x => x.CarbosTotal).HasPrecision(10, 2);
            e.Property(x => x.GrasasTotal).HasPrecision(10, 2);
        });
```

- [ ] **Step 4: Compilar Infrastructure**

Run: `dotnet build "backend/src/Calorias.Infrastructure/Calorias.Infrastructure.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Infrastructure/Persistence/CaloriasDbContext.cs
git -C backend commit -m "feat(infra): mapping de ResumenDiario, Tipo e indices"
```

---

### Task 4: Migración EF

**Files:**
- Create: `backend/src/Calorias.Infrastructure/Migrations/*_TipoComidaYResumenDiario.cs` (generado)

- [ ] **Step 1: Generar la migración**

Run (en PowerShell, desde la carpeta raíz del workspace `App Calorias`):

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet ef migrations add TipoComidaYResumenDiario `
  --project "backend/src/Calorias.Infrastructure" `
  --startup-project "backend/src/Calorias.Api"
```

Expected: `Done.` y archivos nuevos en `src/Calorias.Infrastructure/Migrations/`.

- [ ] **Step 2: Revisar la migración generada**

Abrir el `*_TipoComidaYResumenDiario.cs` y verificar que:
- crea la tabla `ResumenesDiarios` con índice único `(UsuarioId, FechaLocal)`,
- añade columnas `Tipo` (text) y `FechaLocal` (date) a `Registros`,
- añade índice `(UsuarioId, FechaLocal)` en `Registros`.

Si algo no aparece, revisar Task 3 y regenerar (`dotnet ef migrations remove` y repetir Step 1).

- [ ] **Step 3: Aplicar la migración a la BD de desarrollo**

Run:

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
git -C backend commit -m "feat(infra): migracion TipoComidaYResumenDiario"
```

---

### Task 5: Servicio de rollup `ServicioResumenDiario` (TDD)

**Files:**
- Create: `backend/src/Calorias.Application/Abstractions/IServicioResumenDiario.cs`
- Create: `backend/src/Calorias.Infrastructure/Servicios/ServicioResumenDiario.cs`
- Modify: `backend/src/Calorias.Infrastructure/DependencyInjection.cs`
- Create: `backend/tests/Calorias.Tests/Calorias.Tests.csproj`
- Create: `backend/tests/Calorias.Tests/ServicioResumenDiarioTests.cs`

- [ ] **Step 1: Definir la interfaz**

Crear `backend/src/Calorias.Application/Abstractions/IServicioResumenDiario.cs`:

```csharp
namespace Calorias.Application.Abstractions;

public interface IServicioResumenDiario
{
    /// <summary>Recalcula (upsert/borra) el ResumenDiario de un usuario en un día concreto.</summary>
    Task RecalcularDiaAsync(string usuarioId, DateOnly fechaLocal, CancellationToken ct = default);
}
```

- [ ] **Step 2: Crear el proyecto de tests y referenciar los proyectos + EF InMemory**

Run (PowerShell, desde la carpeta raíz del workspace `App Calorias`):

```powershell
dotnet new xunit -o "backend/tests/Calorias.Tests"
dotnet add "backend/tests/Calorias.Tests/Calorias.Tests.csproj" reference `
  "backend/src/Calorias.Domain/Calorias.Domain.csproj" `
  "backend/src/Calorias.Application/Calorias.Application.csproj" `
  "backend/src/Calorias.Infrastructure/Calorias.Infrastructure.csproj"
dotnet add "backend/tests/Calorias.Tests/Calorias.Tests.csproj" package Microsoft.EntityFrameworkCore.InMemory
```

Expected: proyecto creado y paquetes restaurados. Borrar el `UnitTest1.cs` que genera la plantilla:

```powershell
Remove-Item "backend/tests/Calorias.Tests/UnitTest1.cs"
```

- [ ] **Step 3: Escribir los tests que fallan**

Crear `backend/tests/Calorias.Tests/ServicioResumenDiarioTests.cs`:

```csharp
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Calorias.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calorias.Tests;

public class ServicioResumenDiarioTests
{
    private static CaloriasDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<CaloriasDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options);

    private static RegistroComida Comida(string usuario, DateOnly dia, TipoComida tipo,
        decimal kcal, decimal prot, decimal carbs, decimal grasas) => new()
    {
        UsuarioId = usuario,
        FechaLocal = dia,
        Tipo = tipo,
        CaloriasTotales = kcal,
        ProteinasTotales = prot,
        CarbohidratosTotales = carbs,
        GrasasTotales = grasas,
    };

    [Fact]
    public async Task Recalcular_suma_los_registros_del_dia()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 7);
        db.Registros.AddRange(
            Comida("u1", dia, TipoComida.Desayuno, 300, 10, 40, 8),
            Comida("u1", dia, TipoComida.Almuerzo, 700, 30, 80, 20));
        await db.SaveChangesAsync();

        await new ServicioResumenDiario(db).RecalcularDiaAsync("u1", dia);

        var r = await db.ResumenesDiarios.SingleAsync();
        Assert.Equal(1000m, r.CaloriasTotal);
        Assert.Equal(40m, r.ProteinasTotal);
        Assert.Equal(120m, r.CarbosTotal);
        Assert.Equal(28m, r.GrasasTotal);
        Assert.Equal(2, r.NumComidas);
    }

    [Fact]
    public async Task Recalcular_es_idempotente_y_actualiza_el_existente()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 7);
        db.Registros.Add(Comida("u1", dia, TipoComida.Cena, 500, 20, 50, 15));
        await db.SaveChangesAsync();
        var svc = new ServicioResumenDiario(db);

        await svc.RecalcularDiaAsync("u1", dia);
        await svc.RecalcularDiaAsync("u1", dia); // segunda vez: no duplica

        var r = await db.ResumenesDiarios.SingleAsync();
        Assert.Equal(500m, r.CaloriasTotal);
        Assert.Equal(1, r.NumComidas);
    }

    [Fact]
    public async Task Recalcular_borra_el_resumen_si_no_quedan_registros()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 7);
        db.ResumenesDiarios.Add(new ResumenDiario
        {
            UsuarioId = "u1", FechaLocal = dia, CaloriasTotal = 999, NumComidas = 1
        });
        await db.SaveChangesAsync();

        await new ServicioResumenDiario(db).RecalcularDiaAsync("u1", dia);

        Assert.False(await db.ResumenesDiarios.AnyAsync());
    }
}
```

- [ ] **Step 4: Ejecutar los tests y verificar que NO compilan/fallan**

Run: `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"`
Expected: FALLA de compilación — `ServicioResumenDiario` no existe todavía.

- [ ] **Step 5: Implementar el servicio**

Crear `backend/src/Calorias.Infrastructure/Servicios/ServicioResumenDiario.cs`:

```csharp
using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Mantiene el rollup ResumenDiario con estrategia "recompute-the-day": recalcula el
/// total del día desde los registros reales. Idempotente; si el día queda sin comidas,
/// elimina la fila del rollup.
/// </summary>
public class ServicioResumenDiario(CaloriasDbContext db) : IServicioResumenDiario
{
    public async Task RecalcularDiaAsync(
        string usuarioId, DateOnly fechaLocal, CancellationToken ct = default)
    {
        var registros = await db.Registros
            .Where(r => r.UsuarioId == usuarioId && r.FechaLocal == fechaLocal)
            .ToListAsync(ct);

        var existente = await db.ResumenesDiarios
            .FirstOrDefaultAsync(x => x.UsuarioId == usuarioId && x.FechaLocal == fechaLocal, ct);

        if (registros.Count == 0)
        {
            if (existente is not null) db.ResumenesDiarios.Remove(existente);
            await db.SaveChangesAsync(ct);
            return;
        }

        var resumen = existente ?? new ResumenDiario { UsuarioId = usuarioId, FechaLocal = fechaLocal };
        resumen.CaloriasTotal  = registros.Sum(r => r.CaloriasTotales);
        resumen.ProteinasTotal = registros.Sum(r => r.ProteinasTotales);
        resumen.CarbosTotal    = registros.Sum(r => r.CarbohidratosTotales);
        resumen.GrasasTotal    = registros.Sum(r => r.GrasasTotales);
        resumen.NumComidas     = registros.Count;

        if (existente is null) db.ResumenesDiarios.Add(resumen);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 6: Ejecutar los tests y verificar que pasan**

Run: `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"`
Expected: PASS (3 tests).

- [ ] **Step 7: Registrar el servicio en DI**

En `backend/src/Calorias.Infrastructure/DependencyInjection.cs`, debajo de
`services.AddScoped<IServicioUsuarios, ServicioUsuarios>();` añadir:

```csharp
        services.AddScoped<IServicioResumenDiario, ServicioResumenDiario>();
```

- [ ] **Step 8: Compilar la solución de Infra/Api**

Run: `dotnet build "backend/src/Calorias.Api/Calorias.Api.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 9: Commit**

```bash
git -C backend add src/Calorias.Application/Abstractions/IServicioResumenDiario.cs src/Calorias.Infrastructure/Servicios/ServicioResumenDiario.cs src/Calorias.Infrastructure/DependencyInjection.cs tests
git -C backend commit -m "feat(infra): ServicioResumenDiario (rollup recompute-the-day) + tests"
```

---

### Task 6: Cablear tipo/fecha y el rollup en el endpoint de análisis

**Files:**
- Modify: `backend/src/Calorias.Api/Controllers/ComidasController.cs`
- Modify: `backend/src/Calorias.Api/Dtos/AnalisisComidaDto.cs`

- [ ] **Step 1: Añadir `Tipo` y `FechaLocal` al DTO de respuesta**

En `backend/src/Calorias.Api/Dtos/AnalisisComidaDto.cs`, cambiar el record `AnalisisComidaDto` para incluir los dos campos nuevos al inicio:

```csharp
public record AnalisisComidaDto(
    Guid Id,
    string Tipo,
    string FechaLocal,
    string Fecha,
    decimal CaloriasTotales,
    decimal ProteinasTotales,
    decimal CarbohidratosTotales,
    decimal GrasasTotales,
    IReadOnlyList<DetalleComidaDto> Detalles);
```

(El record `DetalleComidaDto` no cambia.)

- [ ] **Step 2: Inyectar el servicio de rollup y aceptar tipo/fechaLocal**

En `backend/src/Calorias.Api/Controllers/ComidasController.cs`, añadir al constructor primario
el parámetro `IServicioResumenDiario resumen` (junto a los existentes):

```csharp
public class ComidasController(
    OrquestadorAnalisisComida orquestador,
    IServicioUsuarios usuarios,
    IServicioResumenDiario resumen,
    CaloriasDbContext db) : ControllerBase
```

Cambiar la firma de `Analizar` para recibir tipo y fecha local desde el form:

```csharp
    [HttpPost("analizar")]
    [RequestSizeLimit(10_000_000)] // 10 MB
    public async Task<IActionResult> Analizar(
        [FromForm] IFormFile foto,
        [FromForm] TipoComida tipo,
        [FromForm] DateOnly fechaLocal,
        CancellationToken ct)
```

Añadir el `using` necesario al inicio del archivo (junto a los demás `using`):

```csharp
using Calorias.Domain.Entities;
```

- [ ] **Step 3: Fijar tipo/fecha, guardar y actualizar el rollup en una transacción**

Reemplazar el bloque que va desde `await using var stream = foto.OpenReadStream();`
hasta `await db.SaveChangesAsync(ct);` por:

```csharp
        await using var stream = foto.OpenReadStream();
        var registro = await orquestador.AnalizarAsync(stream, usuario.Id, ct);
        registro.Tipo = tipo;
        registro.FechaLocal = fechaLocal;

        // Guardado de la comida + recálculo del rollup en la misma transacción.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Registros.Add(registro);
        await db.SaveChangesAsync(ct);
        await resumen.RecalcularDiaAsync(usuario.Id, fechaLocal, ct);
        await tx.CommitAsync(ct);
```

- [ ] **Step 4: Incluir tipo/fecha en el DTO de respuesta**

En el mismo método, en la construcción de `var dto = new AnalisisComidaDto(`, añadir los dos
primeros argumentos nuevos para que coincida con el record actualizado:

```csharp
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
```

- [ ] **Step 5: Compilar la API**

Run: `dotnet build "backend/src/Calorias.Api/Calorias.Api.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 6: Verificación manual (con backend corriendo)**

Arrancar el backend (`ASPNETCORE_ENVIRONMENT=Development`, perfil http) y enviar un análisis
desde el frontend (Task 7) o con curl multipart incluyendo `tipo=Almuerzo` y
`fechaLocal=2026-06-07`. Verificar en la BD que `Registros.Tipo`/`FechaLocal` se guardan y que
`ResumenesDiarios` tiene la fila del día con el total correcto.

- [ ] **Step 7: Commit**

```bash
git -C backend add src/Calorias.Api/Controllers/ComidasController.cs src/Calorias.Api/Dtos/AnalisisComidaDto.cs
git -C backend commit -m "feat(api): analizar fija tipo/fechaLocal y actualiza el rollup"
```

---

### Task 7: Selector de tipo de comida en el frontend

**Files:**
- Modify: `frontend/src/api/comidas.ts`
- Modify: `frontend/src/screens/CaptureScreen.tsx`

- [ ] **Step 1: Extender `analizarFoto` para enviar tipo y fecha local**

En `frontend/src/api/comidas.ts`, añadir el tipo exportado y cambiar la firma de `analizarFoto`.

Añadir cerca de los demás tipos exportados (p. ej. tras `export interface DetalleComida {...}`):

```typescript
export type TipoComida = 'Desayuno' | 'Almuerzo' | 'Cena' | 'Snack';
```

Cambiar la firma y el cuerpo de `analizarFoto` para aceptar `tipo` y adjuntar `tipo` +
`fechaLocal` al FormData. La firma pasa a:

```typescript
export async function analizarFoto(
  fotoUri: string,
  idToken: string,
  tipo: TipoComida,
): Promise<RegistroComida> {
```

Justo después de construir el `form` y adjuntar `foto` (en ambas ramas web/native), añadir:

```typescript
  // Día local del dispositivo en formato YYYY-MM-DD (sin desfase de zona horaria).
  const ahora = new Date();
  const fechaLocal = `${ahora.getFullYear()}-${String(ahora.getMonth() + 1).padStart(2, '0')}-${String(ahora.getDate()).padStart(2, '0')}`;
  form.append('tipo', tipo);
  form.append('fechaLocal', fechaLocal);
```

- [ ] **Step 2: Añadir estado y selector de tipo en `CaptureScreen`**

En `frontend/src/screens/CaptureScreen.tsx`:

Actualizar el import del API para traer el tipo:

```typescript
import { analizarFoto, ApiError, type RegistroComida, type TipoComida } from '../api/comidas';
```

Añadir el estado del tipo seleccionado (junto a los otros `useState`), con `Almuerzo` por defecto:

```typescript
  const [tipo, setTipo] = useState<TipoComida>('Almuerzo');
```

Pasar `tipo` a la llamada en `analizar` (cambiar la línea `const data = await analizarFoto(fotoUri, auth.idToken);`):

```typescript
      const data = await analizarFoto(fotoUri, auth.idToken, tipo);
```

- [ ] **Step 3: Renderizar los chips de tipo de comida**

En el JSX, justo después de `<AuthBanner auth={auth} />` y antes de `<View style={styles.actionsRow}>`, añadir:

```tsx
      <View style={styles.tipoRow}>
        {(['Desayuno', 'Almuerzo', 'Cena', 'Snack'] as TipoComida[]).map((t) => (
          <Pressable
            key={t}
            onPress={() => setTipo(t)}
            style={({ pressed }) => [
              styles.tipoChip,
              tipo === t && styles.tipoChipActive,
              pressed && styles.pressed,
            ]}
          >
            <Text style={[styles.tipoChipText, tipo === t && styles.tipoChipTextActive]}>{t}</Text>
          </Pressable>
        ))}
      </View>
```

- [ ] **Step 4: Añadir los estilos de los chips**

Dentro de `StyleSheet.create({ ... })`, añadir estas entradas (p. ej. tras `actionsRow`):

```typescript
  tipoRow: { flexDirection: 'row', gap: spacing.xs, flexWrap: 'wrap' },
  tipoChip: {
    paddingVertical: spacing.xs,
    paddingHorizontal: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.surfaceAlt,
    borderWidth: 1,
    borderColor: colors.border,
  },
  tipoChipActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  tipoChipText: { color: colors.textMuted, fontSize: 13, fontWeight: '600' },
  tipoChipTextActive: { color: colors.bg },
```

- [ ] **Step 5: Verificar tipos**

Run: `npx tsc --noEmit --project frontend/tsconfig.json`
Expected: sin errores.

- [ ] **Step 6: Verificación manual por web**

Arrancar frontend (`npm run web`) y backend. Seleccionar un tipo, analizar una foto, y
confirmar que la respuesta incluye `tipo`/`fechaLocal` y que el rollup del día se actualiza.

- [ ] **Step 7: Commit**

```bash
git -C frontend add src/api/comidas.ts src/screens/CaptureScreen.tsx
git -C frontend commit -m "feat(captura): selector de tipo de comida y envio de fechaLocal"
```

---

## Verificación final de la Fase A

- [ ] `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"` → PASS.
- [ ] `npx tsc --noEmit --project frontend/tsconfig.json` → sin errores.
- [ ] Flujo manual: capturar una comida con tipo → `Registros` guarda Tipo/FechaLocal y
  `ResumenesDiarios` refleja el total del día. Borrar/añadir otra comida del mismo día
  recalcula el rollup correctamente.
