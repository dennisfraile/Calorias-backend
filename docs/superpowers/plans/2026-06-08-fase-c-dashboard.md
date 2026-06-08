# Fase C — Dashboard comparativo (GET /api/comidas/resumen + pantalla Dashboard) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exponer `GET /api/comidas/resumen` que, para un período (diario/semanal/mensual/trimestral/semestral/anual), devuelve el período del ancla **y** el anterior con promedio kcal/día, déficit vs meta, macros y buckets; y una pantalla **Dashboard** (layout B, comparativa lado a lado con barras dibujadas con `View`s).

**Architecture:** .NET 10 Clean Architecture. Un helper **puro** de dominio `RangosPeriodo` calcula los rangos de fechas (actual + anterior) por período (unit-testeable sin DB). Un servicio `ServicioResumenPeriodos` (Infrastructure) agrega desde el rollup `ResumenDiario` (Fase A) y, para el período diario, desde `RegistroComida` agrupado por `Tipo`; la línea de meta sale de `CalculadoraObjetivos` (Fase B) sobre el perfil del usuario. El frontend añade `api/resumen.ts`, una pantalla `DashboardScreen` (RN clásico, barras con `View`) y un tercer tab en la cabecera.

**Tech Stack:** C# / EF Core 10 (Npgsql/PostgreSQL), xUnit + EF Core InMemory para tests, Expo/React Native (TypeScript) en el frontend.

**Spec:** `docs/superpowers/specs/2026-06-07-comidas-objetivos-dashboards-design.md` (secciones 6.4, 7, 8).

**Rutas base:**
- Backend: `c:\Users\LENOVO\Documents\Portafolio\App Calorias\backend`
- Frontend: `c:\Users\LENOVO\Documents\Portafolio\App Calorias\frontend`

> **Nota:** los tests son in-memory (xUnit + EF InMemory); no requieren la BD ni `ASPNETCORE_ENVIRONMENT`. Para arrancar la API sí: `$env:ASPNETCORE_ENVIRONMENT='Development'` + `$env:GOOGLE_APPLICATION_CREDENTIALS=...`. Parar la API antes de cualquier build que la tenga corriendo.

> **Serialización:** ASP.NET Core serializa en camelCase, así que los records de resultado (`PromedioKcalDia`, etc.) llegan al cliente como `promedioKcalDia`. El frontend consume esa forma.

## Contrato de la respuesta (referencia, spec 6.4)
```json
{
  "periodo": "semanal",
  "actual":   { "promedioKcalDia": 1850, "deficitMedioVsMeta": -200,
                "macros": { "proteinaG": 120, "carbosG": 190, "grasasG": 60 },
                "buckets": [ { "etiqueta": "L", "kcal": 1700, "proteinaG": 110, "carbosG": 180, "grasasG": 55 } ] },
  "anterior": { "promedioKcalDia": 2010, "deficitMedioVsMeta": -40, "macros": { "...": "" }, "buckets": [ "..." ] },
  "cambioPct": -8.0,
  "meta": { "calorias": 2050, "proteinaG": 154, "carbosG": 205, "grasasG": 68 }
}
```
Granularidad de buckets: Diario → por comida (Desayuno/Almuerzo/Cena/Snack); Semanal → 7 días; Mensual → días del mes; Trimestral → 3 meses; Semestral → 6 meses; Anual → 12 meses. `promedioKcalDia` = media sobre **días con registro**; `deficitMedioVsMeta` = `promedioKcalDia − meta.calorias` (null si no hay meta). `cambioPct` = variación % del promedio actual vs anterior (null si el anterior es 0). `meta` = null si el perfil está incompleto.

## File Structure
**Backend:**
- `src/Calorias.Domain/Entities/Periodo.cs` — enum `Periodo` (nuevo).
- `src/Calorias.Domain/Servicios/RangosPeriodo.cs` — cálculo puro de rangos (nuevo).
- `src/Calorias.Application/Resultados/ResumenPeriodos.cs` — records de resultado (nuevo).
- `src/Calorias.Application/Abstractions/IServicioResumenPeriodos.cs` — interfaz (nuevo).
- `src/Calorias.Infrastructure/Servicios/ServicioResumenPeriodos.cs` — agregación (nuevo).
- `src/Calorias.Infrastructure/DependencyInjection.cs` — registrar el servicio (modificar).
- `src/Calorias.Api/Controllers/ComidasController.cs` — endpoint `resumen` (modificar).
- `tests/Calorias.Tests/RangosPeriodoTests.cs` — tests del helper (nuevo).
- `tests/Calorias.Tests/ServicioResumenPeriodosTests.cs` — tests del servicio (nuevo).

**Frontend:**
- `src/api/resumen.ts` — cliente `getResumen` + tipos (nuevo).
- `src/screens/DashboardScreen.tsx` — pantalla layout B (nuevo).
- `App.tsx` — tercer tab "Dashboard" (modificar).

---

### Task 1: Enum `Periodo` y helper puro `RangosPeriodo` (TDD)

**Files:**
- Create: `backend/src/Calorias.Domain/Entities/Periodo.cs`
- Create: `backend/src/Calorias.Domain/Servicios/RangosPeriodo.cs`
- Create: `backend/tests/Calorias.Tests/RangosPeriodoTests.cs`

- [ ] **Step 1: Crear el enum**

Crear `backend/src/Calorias.Domain/Entities/Periodo.cs`:

```csharp
namespace Calorias.Domain.Entities;

public enum Periodo
{
    Diario,
    Semanal,
    Mensual,
    Trimestral,
    Semestral,
    Anual
}
```

- [ ] **Step 2: Escribir los tests que fallan**

Crear `backend/tests/Calorias.Tests/RangosPeriodoTests.cs`:

```csharp
using Calorias.Domain.Entities;
using Calorias.Domain.Servicios;
using Xunit;

namespace Calorias.Tests;

public class RangosPeriodoTests
{
    [Fact]
    public void Diario_actual_y_anterior_son_un_dia()
    {
        var r = RangosPeriodo.Calcular(Periodo.Diario, new DateOnly(2026, 6, 8));
        Assert.Equal(new DateOnly(2026, 6, 8), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 6, 8), r.ActualFin);
        Assert.Equal(new DateOnly(2026, 6, 7), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2026, 6, 7), r.AnteriorFin);
    }

    [Fact]
    public void Semanal_empieza_en_lunes_y_cubre_siete_dias()
    {
        var ancla = new DateOnly(2026, 6, 10);
        var r = RangosPeriodo.Calcular(Periodo.Semanal, ancla);
        Assert.Equal(DayOfWeek.Monday, r.ActualInicio.DayOfWeek);
        Assert.Equal(r.ActualInicio.AddDays(6), r.ActualFin);
        Assert.True(r.ActualInicio <= ancla && ancla <= r.ActualFin);
        Assert.Equal(r.ActualInicio.AddDays(-7), r.AnteriorInicio);
        Assert.Equal(r.ActualInicio.AddDays(-1), r.AnteriorFin);
    }

    [Fact]
    public void Mensual_cubre_el_mes_y_el_anterior()
    {
        var r = RangosPeriodo.Calcular(Periodo.Mensual, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 6, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 6, 30), r.ActualFin);
        Assert.Equal(new DateOnly(2026, 5, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2026, 5, 31), r.AnteriorFin);
    }

    [Fact]
    public void Mensual_en_enero_el_anterior_es_diciembre_previo()
    {
        var r = RangosPeriodo.Calcular(Periodo.Mensual, new DateOnly(2026, 1, 10));
        Assert.Equal(new DateOnly(2026, 1, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 1, 31), r.ActualFin);
        Assert.Equal(new DateOnly(2025, 12, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2025, 12, 31), r.AnteriorFin);
    }

    [Fact]
    public void Trimestral_q2_y_q1()
    {
        var r = RangosPeriodo.Calcular(Periodo.Trimestral, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 4, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 6, 30), r.ActualFin);
        Assert.Equal(new DateOnly(2026, 1, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2026, 3, 31), r.AnteriorFin);
    }

    [Fact]
    public void Semestral_h1_y_h2_previo()
    {
        var r = RangosPeriodo.Calcular(Periodo.Semestral, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 1, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 6, 30), r.ActualFin);
        Assert.Equal(new DateOnly(2025, 7, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2025, 12, 31), r.AnteriorFin);
    }

    [Fact]
    public void Anual_actual_y_anterior()
    {
        var r = RangosPeriodo.Calcular(Periodo.Anual, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 1, 1), r.ActualInicio);
        Assert.Equal(new DateOnly(2026, 12, 31), r.ActualFin);
        Assert.Equal(new DateOnly(2025, 1, 1), r.AnteriorInicio);
        Assert.Equal(new DateOnly(2025, 12, 31), r.AnteriorFin);
    }
}
```

- [ ] **Step 3: Ejecutar y verificar que NO compila (falla)**

Run: `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"`
Expected: FALLA de compilación — `RangosPeriodo` no existe.

- [ ] **Step 4: Implementar el helper**

Crear `backend/src/Calorias.Domain/Servicios/RangosPeriodo.cs`:

```csharp
using Calorias.Domain.Entities;

namespace Calorias.Domain.Servicios;

/// <summary>Rango del período actual y del inmediatamente anterior (fechas inclusivas).</summary>
public record RangoComparativo(
    DateOnly ActualInicio,
    DateOnly ActualFin,
    DateOnly AnteriorInicio,
    DateOnly AnteriorFin);

/// <summary>
/// Calcula, para un período y una fecha ancla, el rango [inicio, fin] del período que
/// contiene al ancla y el del período anterior. Puro: sin estado ni dependencias.
/// </summary>
public static class RangosPeriodo
{
    public static RangoComparativo Calcular(Periodo periodo, DateOnly ancla)
    {
        switch (periodo)
        {
            case Periodo.Diario:
            {
                var ant = ancla.AddDays(-1);
                return new RangoComparativo(ancla, ancla, ant, ant);
            }
            case Periodo.Semanal:
            {
                int offsetLunes = ((int)ancla.DayOfWeek + 6) % 7; // domingo=0 -> 6; lunes=1 -> 0
                var ini = ancla.AddDays(-offsetLunes);
                var fin = ini.AddDays(6);
                return new RangoComparativo(ini, fin, ini.AddDays(-7), ini.AddDays(-1));
            }
            case Periodo.Mensual:
            {
                var ini = new DateOnly(ancla.Year, ancla.Month, 1);
                var fin = ini.AddMonths(1).AddDays(-1);
                return new RangoComparativo(ini, fin, ini.AddMonths(-1), ini.AddDays(-1));
            }
            case Periodo.Trimestral:
            {
                int mesInicio = (ancla.Month - 1) / 3 * 3 + 1;
                var ini = new DateOnly(ancla.Year, mesInicio, 1);
                var fin = ini.AddMonths(3).AddDays(-1);
                return new RangoComparativo(ini, fin, ini.AddMonths(-3), ini.AddDays(-1));
            }
            case Periodo.Semestral:
            {
                int mesInicio = ancla.Month <= 6 ? 1 : 7;
                var ini = new DateOnly(ancla.Year, mesInicio, 1);
                var fin = ini.AddMonths(6).AddDays(-1);
                return new RangoComparativo(ini, fin, ini.AddMonths(-6), ini.AddDays(-1));
            }
            case Periodo.Anual:
            {
                var ini = new DateOnly(ancla.Year, 1, 1);
                var fin = new DateOnly(ancla.Year, 12, 31);
                return new RangoComparativo(ini, fin, ini.AddYears(-1), ini.AddDays(-1));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(periodo), periodo, "Período no soportado.");
        }
    }
}
```

- [ ] **Step 5: Ejecutar los tests y verificar que pasan**

Run: `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"`
Expected: PASS (los previos + 7 nuevos de `RangosPeriodo`).

- [ ] **Step 6: Commit**

```bash
git -C backend add src/Calorias.Domain/Entities/Periodo.cs src/Calorias.Domain/Servicios/RangosPeriodo.cs tests/Calorias.Tests/RangosPeriodoTests.cs
git -C backend commit -m "feat(domain): Periodo y RangosPeriodo (rangos actual/anterior) + tests"
```

---

### Task 2: Records de resultado e interfaz del servicio

**Files:**
- Create: `backend/src/Calorias.Application/Resultados/ResumenPeriodos.cs`
- Create: `backend/src/Calorias.Application/Abstractions/IServicioResumenPeriodos.cs`

- [ ] **Step 1: Crear los records de resultado**

Crear `backend/src/Calorias.Application/Resultados/ResumenPeriodos.cs`:

```csharp
namespace Calorias.Application.Resultados;

public record MacrosResumen(int ProteinaG, int CarbosG, int GrasasG);

public record BucketResumen(string Etiqueta, int Kcal, int ProteinaG, int CarbosG, int GrasasG);

public record LadoPeriodo(
    int PromedioKcalDia,
    int? DeficitMedioVsMeta,
    MacrosResumen Macros,
    IReadOnlyList<BucketResumen> Buckets);

public record MetaResumen(int Calorias, int ProteinaG, int CarbosG, int GrasasG);

public record ResumenPeriodos(
    string Periodo,
    LadoPeriodo Actual,
    LadoPeriodo Anterior,
    double? CambioPct,
    MetaResumen? Meta);
```

- [ ] **Step 2: Crear la interfaz**

Crear `backend/src/Calorias.Application/Abstractions/IServicioResumenPeriodos.cs`:

```csharp
using Calorias.Application.Resultados;
using Calorias.Domain.Entities;

namespace Calorias.Application.Abstractions;

public interface IServicioResumenPeriodos
{
    /// <summary>Resumen comparativo (actual vs anterior) del período, con meta del perfil.</summary>
    Task<ResumenPeriodos> ObtenerAsync(
        string usuarioId, Periodo periodo, DateOnly ancla, CancellationToken ct = default);
}
```

- [ ] **Step 3: Compilar Application**

Run: `dotnet build "backend/src/Calorias.Application/Calorias.Application.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git -C backend add src/Calorias.Application/Resultados/ResumenPeriodos.cs src/Calorias.Application/Abstractions/IServicioResumenPeriodos.cs
git -C backend commit -m "feat(app): records de resultado e interfaz IServicioResumenPeriodos"
```

---

### Task 3: `ServicioResumenPeriodos` (agregación) + tests (TDD)

**Files:**
- Create: `backend/src/Calorias.Infrastructure/Servicios/ServicioResumenPeriodos.cs`
- Modify: `backend/src/Calorias.Infrastructure/DependencyInjection.cs`
- Create: `backend/tests/Calorias.Tests/ServicioResumenPeriodosTests.cs`

- [ ] **Step 1: Escribir los tests que fallan**

Crear `backend/tests/Calorias.Tests/ServicioResumenPeriodosTests.cs`:

```csharp
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Calorias.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calorias.Tests;

public class ServicioResumenPeriodosTests
{
    private static CaloriasDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<CaloriasDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options);

    private static ResumenDiario Dia(string u, DateOnly d, decimal kcal, decimal prot, decimal carb, decimal gr) =>
        new() { UsuarioId = u, FechaLocal = d, CaloriasTotal = kcal, ProteinasTotal = prot, CarbosTotal = carb, GrasasTotal = gr, NumComidas = 1 };

    [Fact]
    public async Task Semanal_promedia_los_dias_con_registro_y_arma_siete_buckets()
    {
        using var db = NuevaDb();
        // Semana lunes 2026-06-08 .. domingo 2026-06-14.
        db.ResumenesDiarios.AddRange(
            Dia("u1", new DateOnly(2026, 6, 8), 2000, 100, 200, 60),  // lunes
            Dia("u1", new DateOnly(2026, 6, 9), 1000, 50, 100, 30),   // martes
            Dia("u1", new DateOnly(2026, 6, 10), 1500, 75, 150, 45)); // miércoles
        await db.SaveChangesAsync();

        var r = await new ServicioResumenPeriodos(db)
            .ObtenerAsync("u1", Periodo.Semanal, new DateOnly(2026, 6, 10));

        // Promedio sobre 3 días con registro: (2000+1000+1500)/3 = 1500.
        Assert.Equal(1500, r.Actual.PromedioKcalDia);
        Assert.Equal(7, r.Actual.Buckets.Count);
        // Bucket del lunes (índice 0) = total de ese día.
        Assert.Equal(2000, r.Actual.Buckets[0].Kcal);
        Assert.Equal(1000, r.Actual.Buckets[1].Kcal);
        Assert.Equal(0, r.Actual.Buckets[3].Kcal); // jueves sin datos
        Assert.Equal("L", r.Actual.Buckets[0].Etiqueta);
        Assert.Equal("D", r.Actual.Buckets[6].Etiqueta);
    }

    [Fact]
    public async Task Sin_datos_el_promedio_es_cero_y_no_revienta()
    {
        using var db = NuevaDb();
        var r = await new ServicioResumenPeriodos(db)
            .ObtenerAsync("u1", Periodo.Semanal, new DateOnly(2026, 6, 10));
        Assert.Equal(0, r.Actual.PromedioKcalDia);
        Assert.Equal(7, r.Actual.Buckets.Count);
        Assert.Null(r.Meta); // sin Usuario/perfil
        Assert.Null(r.CambioPct); // anterior 0
    }

    [Fact]
    public async Task Diario_arma_cuatro_buckets_por_tipo_de_comida()
    {
        using var db = NuevaDb();
        var dia = new DateOnly(2026, 6, 8);
        db.Registros.AddRange(
            new RegistroComida { UsuarioId = "u1", FechaLocal = dia, Tipo = TipoComida.Desayuno, CaloriasTotales = 300, ProteinasTotales = 10, CarbohidratosTotales = 40, GrasasTotales = 8 },
            new RegistroComida { UsuarioId = "u1", FechaLocal = dia, Tipo = TipoComida.Almuerzo, CaloriasTotales = 700, ProteinasTotales = 30, CarbohidratosTotales = 80, GrasasTotales = 20 });
        db.ResumenesDiarios.Add(Dia("u1", dia, 1000, 40, 120, 28));
        await db.SaveChangesAsync();

        var r = await new ServicioResumenPeriodos(db).ObtenerAsync("u1", Periodo.Diario, dia);

        Assert.Equal(4, r.Actual.Buckets.Count);
        Assert.Equal("Desayuno", r.Actual.Buckets[0].Etiqueta);
        Assert.Equal(300, r.Actual.Buckets[0].Kcal);
        Assert.Equal("Almuerzo", r.Actual.Buckets[1].Etiqueta);
        Assert.Equal(700, r.Actual.Buckets[1].Kcal);
        Assert.Equal(0, r.Actual.Buckets[2].Kcal); // Cena sin datos
        Assert.Equal(1000, r.Actual.PromedioKcalDia); // 1 día con registro
    }

    [Fact]
    public async Task CambioPct_compara_promedios_actual_vs_anterior()
    {
        using var db = NuevaDb();
        // Mes actual junio: un día 1000. Mes anterior mayo: un día 2000.
        db.ResumenesDiarios.AddRange(
            Dia("u1", new DateOnly(2026, 6, 5), 1000, 0, 0, 0),
            Dia("u1", new DateOnly(2026, 5, 5), 2000, 0, 0, 0));
        await db.SaveChangesAsync();

        var r = await new ServicioResumenPeriodos(db)
            .ObtenerAsync("u1", Periodo.Mensual, new DateOnly(2026, 6, 15));

        Assert.Equal(1000, r.Actual.PromedioKcalDia);
        Assert.Equal(2000, r.Anterior.PromedioKcalDia);
        // (1000-2000)/2000*100 = -50
        Assert.Equal(-50.0, r.CambioPct);
    }
}
```

- [ ] **Step 2: Ejecutar y verificar que NO compila (falla)**

Run: `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"`
Expected: FALLA de compilación — `ServicioResumenPeriodos` no existe.

- [ ] **Step 3: Implementar el servicio**

Crear `backend/src/Calorias.Infrastructure/Servicios/ServicioResumenPeriodos.cs`:

```csharp
using Calorias.Application.Abstractions;
using Calorias.Application.Resultados;
using Calorias.Domain.Entities;
using Calorias.Domain.Servicios;
using Calorias.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Calcula el resumen comparativo (actual vs anterior) de un período agregando desde el
/// rollup ResumenDiario; para el período Diario los buckets salen de RegistroComida por Tipo.
/// La meta sale de CalculadoraObjetivos sobre el perfil del usuario (null si está incompleto).
/// </summary>
public class ServicioResumenPeriodos(CaloriasDbContext db) : IServicioResumenPeriodos
{
    private static readonly string[] DiasSemana = ["L", "M", "X", "J", "V", "S", "D"];
    private static readonly string[] MesesCorto =
        ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];
    private static readonly TipoComida[] Comidas =
        [TipoComida.Desayuno, TipoComida.Almuerzo, TipoComida.Cena, TipoComida.Snack];

    public async Task<ResumenPeriodos> ObtenerAsync(
        string usuarioId, Periodo periodo, DateOnly ancla, CancellationToken ct = default)
    {
        var rangos = RangosPeriodo.Calcular(periodo, ancla);
        var meta = await CalcularMetaAsync(usuarioId, ct);

        var actual = await ConstruirLadoAsync(usuarioId, periodo, rangos.ActualInicio, rangos.ActualFin, meta, ct);
        var anterior = await ConstruirLadoAsync(usuarioId, periodo, rangos.AnteriorInicio, rangos.AnteriorFin, meta, ct);

        double? cambioPct = anterior.PromedioKcalDia > 0
            ? Math.Round((actual.PromedioKcalDia - anterior.PromedioKcalDia) * 100.0 / anterior.PromedioKcalDia, 1)
            : null;

        return new ResumenPeriodos(periodo.ToString(), actual, anterior, cambioPct, meta);
    }

    private async Task<MetaResumen?> CalcularMetaAsync(string usuarioId, CancellationToken ct)
    {
        var u = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == usuarioId, ct);
        if (u is null) return null;

        bool completo = u.Sexo is not null && u.FechaNacimiento is not null && u.AlturaCm is not null
            && u.PesoKg is not null && u.NivelActividad is not null && u.Objetivo is not null;
        if (!completo) return null;

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        int edad = hoy.Year - u.FechaNacimiento!.Value.Year;
        if (u.FechaNacimiento.Value > hoy.AddYears(-edad)) edad--;

        var r = CalculadoraObjetivos.Calcular(
            u.Sexo!.Value, edad, u.AlturaCm!.Value, u.PesoKg!.Value,
            u.NivelActividad!.Value, u.Objetivo!.Value, u.RitmoKgSemana ?? 0m,
            u.MetaCaloriasOverride, u.MetaProteinaPct, u.MetaCarbosPct, u.MetaGrasasPct);

        return new MetaResumen(r.MetaCalorias, r.ProteinaG, r.CarbosG, r.GrasasG);
    }

    private async Task<LadoPeriodo> ConstruirLadoAsync(
        string u, Periodo periodo, DateOnly inicio, DateOnly fin, MetaResumen? meta, CancellationToken ct)
    {
        var resumenes = await db.ResumenesDiarios.AsNoTracking()
            .Where(r => r.UsuarioId == u && r.FechaLocal >= inicio && r.FechaLocal <= fin)
            .ToListAsync(ct);

        int dias = resumenes.Count;
        int Prom(Func<ResumenDiario, decimal> sel) =>
            dias > 0 ? (int)Math.Round(resumenes.Sum(sel) / dias, MidpointRounding.AwayFromZero) : 0;

        int promedio = Prom(r => r.CaloriasTotal);
        var macros = new MacrosResumen(Prom(r => r.ProteinasTotal), Prom(r => r.CarbosTotal), Prom(r => r.GrasasTotal));
        int? deficit = meta is not null ? promedio - meta.Calorias : null;

        IReadOnlyList<BucketResumen> buckets = periodo switch
        {
            Periodo.Diario => await BucketsPorComidaAsync(u, inicio, ct),
            Periodo.Semanal or Periodo.Mensual => BucketsPorDia(periodo, inicio, fin, resumenes),
            _ => BucketsPorMes(inicio, fin, resumenes),
        };

        return new LadoPeriodo(promedio, deficit, macros, buckets);
    }

    private async Task<IReadOnlyList<BucketResumen>> BucketsPorComidaAsync(
        string u, DateOnly dia, CancellationToken ct)
    {
        var registros = await db.Registros.AsNoTracking()
            .Where(r => r.UsuarioId == u && r.FechaLocal == dia)
            .ToListAsync(ct);

        return Comidas.Select(tipo =>
        {
            var grupo = registros.Where(r => r.Tipo == tipo).ToList();
            return new BucketResumen(
                tipo.ToString(),
                (int)Math.Round(grupo.Sum(r => r.CaloriasTotales), MidpointRounding.AwayFromZero),
                (int)Math.Round(grupo.Sum(r => r.ProteinasTotales), MidpointRounding.AwayFromZero),
                (int)Math.Round(grupo.Sum(r => r.CarbohidratosTotales), MidpointRounding.AwayFromZero),
                (int)Math.Round(grupo.Sum(r => r.GrasasTotales), MidpointRounding.AwayFromZero));
        }).ToList();
    }

    private static IReadOnlyList<BucketResumen> BucketsPorDia(
        Periodo periodo, DateOnly inicio, DateOnly fin, List<ResumenDiario> resumenes)
    {
        var porDia = resumenes.ToDictionary(r => r.FechaLocal);
        var lista = new List<BucketResumen>();
        for (var d = inicio; d <= fin; d = d.AddDays(1))
        {
            string etiqueta = periodo == Periodo.Semanal
                ? DiasSemana[((int)d.DayOfWeek + 6) % 7]
                : d.Day.ToString();
            lista.Add(porDia.TryGetValue(d, out var r)
                ? new BucketResumen(etiqueta, (int)r.CaloriasTotal, (int)r.ProteinasTotal, (int)r.CarbosTotal, (int)r.GrasasTotal)
                : new BucketResumen(etiqueta, 0, 0, 0, 0));
        }
        return lista;
    }

    private static IReadOnlyList<BucketResumen> BucketsPorMes(
        DateOnly inicio, DateOnly fin, List<ResumenDiario> resumenes)
    {
        var lista = new List<BucketResumen>();
        for (var m = new DateOnly(inicio.Year, inicio.Month, 1); m <= fin; m = m.AddMonths(1))
        {
            var delMes = resumenes.Where(r => r.FechaLocal.Year == m.Year && r.FechaLocal.Month == m.Month).ToList();
            lista.Add(new BucketResumen(
                MesesCorto[m.Month - 1],
                (int)Math.Round(delMes.Sum(r => r.CaloriasTotal), MidpointRounding.AwayFromZero),
                (int)Math.Round(delMes.Sum(r => r.ProteinasTotal), MidpointRounding.AwayFromZero),
                (int)Math.Round(delMes.Sum(r => r.CarbosTotal), MidpointRounding.AwayFromZero),
                (int)Math.Round(delMes.Sum(r => r.GrasasTotal), MidpointRounding.AwayFromZero)));
        }
        return lista;
    }
}
```

- [ ] **Step 4: Registrar el servicio en DI**

En `backend/src/Calorias.Infrastructure/DependencyInjection.cs`, debajo de
`services.AddScoped<IServicioResumenDiario, ServicioResumenDiario>();` añadir:

```csharp
        services.AddScoped<IServicioResumenPeriodos, ServicioResumenPeriodos>();
```

- [ ] **Step 5: Ejecutar los tests y verificar que pasan**

Run: `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"`
Expected: PASS (previos + 4 nuevos del servicio).

- [ ] **Step 6: Commit**

```bash
git -C backend add src/Calorias.Infrastructure/Servicios/ServicioResumenPeriodos.cs src/Calorias.Infrastructure/DependencyInjection.cs tests/Calorias.Tests/ServicioResumenPeriodosTests.cs
git -C backend commit -m "feat(infra): ServicioResumenPeriodos (agregacion por periodo) + tests"
```

---

### Task 4: Endpoint `GET /api/comidas/resumen`

**Files:**
- Modify: `backend/src/Calorias.Api/Controllers/ComidasController.cs`

- [ ] **Step 1: Inyectar el servicio en el controller**

En `backend/src/Calorias.Api/Controllers/ComidasController.cs`, añadir el parámetro
`IServicioResumenPeriodos resumenPeriodos` al constructor primario (junto a los existentes):

```csharp
public class ComidasController(
    OrquestadorAnalisisComida orquestador,
    IServicioUsuarios usuarios,
    IServicioResumenDiario resumen,
    IServicioResumenPeriodos resumenPeriodos,
    CaloriasDbContext db) : ControllerBase
```

- [ ] **Step 2: Añadir el endpoint**

En el mismo controller, después del método `Historial`, añadir:

```csharp
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
```

- [ ] **Step 3: Añadir los `using` necesarios**

Verificar que al inicio de `ComidasController.cs` estén (añadir los que falten):

```csharp
using Calorias.Application.Abstractions;
using Calorias.Application.Resultados;
using Calorias.Domain.Entities;
```

(`ClaimTypes` ya se usa en `Historial` vía `System.Security.Claims`; `IServicioResumenPeriodos` está en `Calorias.Application.Abstractions`; `ResumenPeriodos` en `Calorias.Application.Resultados`; `Periodo` en `Calorias.Domain.Entities`.)

- [ ] **Step 4: Compilar la API**

Run: `dotnet build "backend/src/Calorias.Api/Calorias.Api.csproj"`
Expected: `Build succeeded`.

- [ ] **Step 5: Verificación manual (con backend corriendo)**

Arrancar la API (parar instancias previas; `$env:ASPNETCORE_ENVIRONMENT='Development'`,
`$env:GOOGLE_APPLICATION_CREDENTIALS='C:\Users\LENOVO\secrets\calorias-498417-77abb89d3162.json'`,
`dotnet run --project backend/src/Calorias.Api --launch-profile http`). Sin token,
`GET http://localhost:5247/api/comidas/resumen?periodo=semanal` debe devolver **401**.
**Parar la API** al terminar.

- [ ] **Step 6: Commit**

```bash
git -C backend add src/Calorias.Api/Controllers/ComidasController.cs
git -C backend commit -m "feat(api): GET /api/comidas/resumen (dashboard comparativo)"
```

---

### Task 5: Cliente de API del resumen (frontend)

**Files:**
- Create: `frontend/src/api/resumen.ts`

- [ ] **Step 1: Crear `api/resumen.ts`**

Crear `frontend/src/api/resumen.ts`:

```typescript
import { API_BASE_URL } from '../config';
import { ApiError } from './comidas';

export type Periodo = 'diario' | 'semanal' | 'mensual' | 'trimestral' | 'semestral' | 'anual';

export interface MacrosResumen {
  proteinaG: number;
  carbosG: number;
  grasasG: number;
}

export interface BucketResumen {
  etiqueta: string;
  kcal: number;
  proteinaG: number;
  carbosG: number;
  grasasG: number;
}

export interface LadoPeriodo {
  promedioKcalDia: number;
  deficitMedioVsMeta: number | null;
  macros: MacrosResumen;
  buckets: BucketResumen[];
}

export interface MetaResumen {
  calorias: number;
  proteinaG: number;
  carbosG: number;
  grasasG: number;
}

export interface ResumenPeriodos {
  periodo: string;
  actual: LadoPeriodo;
  anterior: LadoPeriodo;
  cambioPct: number | null;
  meta: MetaResumen | null;
}

/** Pide el resumen comparativo. `ancla` es la fecha local del cliente (YYYY-MM-DD). */
export async function getResumen(
  idToken: string,
  periodo: Periodo,
  ancla: string,
): Promise<ResumenPeriodos> {
  const url = `${API_BASE_URL}/api/comidas/resumen?periodo=${periodo}&ancla=${ancla}`;
  let res: Response;
  try {
    res = await fetch(url, {
      method: 'GET',
      headers: { Authorization: `Bearer ${idToken}`, Accept: 'application/json' },
    });
  } catch (e) {
    throw new ApiError(
      `No se pudo conectar con el backend (${url}). ${e instanceof Error ? e.message : ''}`,
      0,
    );
  }
  if (!res.ok) {
    const detalle = await res.text().catch(() => '');
    if (res.status === 401) throw new ApiError('No autorizado (401).', 401);
    throw new ApiError(`El backend respondió ${res.status}. ${detalle.slice(0, 300)}`, res.status);
  }
  return (await res.json()) as ResumenPeriodos;
}
```

- [ ] **Step 2: Verificar tipos**

Run (desde `frontend/`): `.\node_modules\.bin\tsc --noEmit`
Expected: sin errores.

- [ ] **Step 3: Commit**

```bash
git -C frontend add src/api/resumen.ts
git -C frontend commit -m "feat(api): cliente getResumen + tipos del dashboard"
```

---

### Task 6: Pantalla `DashboardScreen` (layout B)

**Files:**
- Create: `frontend/src/screens/DashboardScreen.tsx`

> **Sub-skill UI:** al implementar, seguir `frontend-design:frontend-design` para el acabado.
> El código de abajo es funcional, temificado (useTheme) y dibuja las barras con `View`s (sin
> librería de charts), como pide la spec.

- [ ] **Step 1: Crear `DashboardScreen.tsx`**

Crear `frontend/src/screens/DashboardScreen.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { GoogleAuthState } from '../auth/useGoogleAuth';
import { ApiError } from '../api/comidas';
import { getResumen, type Periodo, type ResumenPeriodos } from '../api/resumen';
import { useTheme } from '../theme-context';
import { radius, spacing, type Palette } from '../theme';

const PERIODOS: { key: Periodo; label: string }[] = [
  { key: 'diario', label: 'Día' },
  { key: 'semanal', label: 'Semana' },
  { key: 'mensual', label: 'Mes' },
  { key: 'trimestral', label: 'Trim.' },
  { key: 'semestral', label: 'Sem.' },
  { key: 'anual', label: 'Año' },
];

function hoyLocal(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export default function DashboardScreen({ auth }: { auth: GoogleAuthState }) {
  const { colors } = useTheme();
  const styles = makeStyles(colors);
  const [periodo, setPeriodo] = useState<Periodo>('semanal');
  const [data, setData] = useState<ResumenPeriodos | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!auth.idToken) {
      setData(null);
      return;
    }
    let activo = true;
    setCargando(true);
    setError(null);
    getResumen(auth.idToken, periodo, hoyLocal())
      .then((r) => {
        if (activo) setData(r);
      })
      .catch((e) => {
        if (activo) setError(e instanceof ApiError ? e.message : 'No se pudo cargar el resumen.');
      })
      .finally(() => {
        if (activo) setCargando(false);
      });
    return () => {
      activo = false;
    };
  }, [auth.idToken, periodo]);

  if (!auth.idToken) {
    return (
      <View style={[styles.centro, { backgroundColor: colors.bg }]}>
        <Text style={styles.aviso}>Debe iniciar sesión para ver tu dashboard.</Text>
      </View>
    );
  }

  return (
    <ScrollView contentContainerStyle={styles.scroll}>
      <View style={styles.container}>
        <Text style={styles.title}>Dashboard</Text>

        <View style={styles.tabs}>
          {PERIODOS.map((p) => (
            <Pressable
              key={p.key}
              onPress={() => setPeriodo(p.key)}
              style={({ pressed }) => [
                styles.tab,
                periodo === p.key && styles.tabActive,
                pressed && styles.pressed,
              ]}
            >
              <Text style={[styles.tabText, periodo === p.key && styles.tabTextActive]}>{p.label}</Text>
            </Pressable>
          ))}
        </View>

        {cargando && <ActivityIndicator color={colors.primary} style={{ marginTop: spacing.lg }} />}
        {error && <Text style={styles.error}>{error}</Text>}

        {data && !cargando && (
          <>
            <Comparativa data={data} colors={colors} styles={styles} />
            <Barras data={data} colors={colors} styles={styles} />
            <MacrosVsMeta data={data} colors={colors} styles={styles} />
          </>
        )}
      </View>
    </ScrollView>
  );
}

type Estilos = ReturnType<typeof makeStyles>;

function Comparativa({ data, colors, styles }: { data: ResumenPeriodos; colors: Palette; styles: Estilos }) {
  const { actual, anterior, cambioPct, meta } = data;
  const sube = (cambioPct ?? 0) > 0;
  return (
    <View style={styles.card}>
      <Text style={styles.cardTitle}>Promedio kcal/día</Text>
      <View style={styles.compRow}>
        <View style={styles.compCol}>
          <Text style={styles.compActual}>{actual.promedioKcalDia}</Text>
          <Text style={styles.compLabel}>Actual</Text>
        </View>
        <View style={styles.compCol}>
          <Text style={styles.compAnterior}>{anterior.promedioKcalDia}</Text>
          <Text style={styles.compLabel}>Anterior</Text>
        </View>
      </View>
      {cambioPct != null && (
        <Text style={[styles.pill, { color: sube ? colors.warning : colors.primary }]}>
          {sube ? '▲' : '▼'} {Math.abs(cambioPct)}% vs período anterior
        </Text>
      )}
      {meta && actual.deficitMedioVsMeta != null && (
        <Text style={styles.metaLine}>
          Meta {meta.calorias} kcal · {actual.deficitMedioVsMeta >= 0 ? '+' : ''}
          {actual.deficitMedioVsMeta} kcal vs meta
        </Text>
      )}
    </View>
  );
}

function Barras({ data, colors, styles }: { data: ResumenPeriodos; colors: Palette; styles: Estilos }) {
  const { actual, anterior, meta } = data;
  const H = 160;
  const maxKcal = Math.max(
    1,
    ...actual.buckets.map((b) => b.kcal),
    ...anterior.buckets.map((b) => b.kcal),
    meta?.calorias ?? 0,
  );
  return (
    <View style={styles.card}>
      <Text style={styles.cardTitle}>Distribución del período</Text>
      <View style={[styles.plot, { height: H }]}>
        {meta && <View style={[styles.metaBar, { bottom: (meta.calorias / maxKcal) * H }]} />}
        <View style={styles.barsRow}>
          {actual.buckets.map((b, i) => {
            const ha = (b.kcal / maxKcal) * H;
            const hp = ((anterior.buckets[i]?.kcal ?? 0) / maxKcal) * H;
            return (
              <View key={i} style={styles.barPair}>
                <View style={[styles.barAnterior, { height: hp }]} />
                <View style={[styles.barActual, { height: ha }]} />
              </View>
            );
          })}
        </View>
      </View>
      <View style={styles.labelsRow}>
        {actual.buckets.map((b, i) => (
          <Text key={i} style={styles.barLabel} numberOfLines={1}>
            {b.etiqueta}
          </Text>
        ))}
      </View>
      <View style={styles.legend}>
        <Leyenda color={colors.primary} text="Actual" styles={styles} />
        <Leyenda color={colors.textMuted} text="Anterior" styles={styles} />
        {meta && <Leyenda color={colors.accent} text="Meta" styles={styles} />}
      </View>
    </View>
  );
}

function Leyenda({ color, text, styles }: { color: string; text: string; styles: Estilos }) {
  return (
    <View style={styles.legendItem}>
      <View style={[styles.legendDot, { backgroundColor: color }]} />
      <Text style={styles.legendText}>{text}</Text>
    </View>
  );
}

function MacrosVsMeta({ data, colors, styles }: { data: ResumenPeriodos; colors: Palette; styles: Estilos }) {
  const { actual, meta } = data;
  if (!meta) {
    return (
      <View style={styles.card}>
        <Text style={styles.cardTitle}>Macros</Text>
        <Text style={styles.metaLine}>Completa tu perfil para ver tus metas de macros.</Text>
      </View>
    );
  }
  const rows: { label: string; a: number; m: number }[] = [
    { label: 'Proteína', a: actual.macros.proteinaG, m: meta.proteinaG },
    { label: 'Carbos', a: actual.macros.carbosG, m: meta.carbosG },
    { label: 'Grasas', a: actual.macros.grasasG, m: meta.grasasG },
  ];
  return (
    <View style={styles.card}>
      <Text style={styles.cardTitle}>Macros (promedio diario vs meta)</Text>
      {rows.map((r) => {
        const pct = Math.min(1, r.m > 0 ? r.a / r.m : 0);
        return (
          <View key={r.label} style={styles.macroRow}>
            <Text style={styles.macroLabel}>{r.label}</Text>
            <View style={styles.macroBarBg}>
              <View style={[styles.macroBarFill, { width: `${pct * 100}%`, backgroundColor: colors.primary }]} />
            </View>
            <Text style={styles.macroVal}>
              {r.a} / {r.m} g
            </Text>
          </View>
        );
      })}
    </View>
  );
}

const makeStyles = (colors: Palette) =>
  StyleSheet.create({
    scroll: { padding: spacing.md, paddingBottom: spacing.xl },
    container: { width: '100%', maxWidth: 760, alignSelf: 'center', gap: spacing.md },
    centro: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg },
    aviso: { color: colors.danger, fontSize: 16, fontWeight: '600', textAlign: 'center' },
    title: { color: colors.text, fontSize: 24, fontWeight: '800' },
    error: { color: colors.danger, fontSize: 13 },
    tabs: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
    tab: {
      paddingVertical: spacing.xs,
      paddingHorizontal: spacing.md,
      borderRadius: radius.md,
      backgroundColor: colors.surfaceAlt,
      borderWidth: 1,
      borderColor: colors.border,
    },
    tabActive: { backgroundColor: colors.primary, borderColor: colors.primary },
    tabText: { color: colors.textMuted, fontSize: 13, fontWeight: '600' },
    tabTextActive: { color: colors.bg },
    card: {
      backgroundColor: colors.surface,
      borderColor: colors.border,
      borderWidth: 1,
      borderRadius: radius.lg,
      padding: spacing.md,
      gap: spacing.sm,
    },
    cardTitle: { color: colors.text, fontSize: 15, fontWeight: '700' },
    compRow: { flexDirection: 'row', justifyContent: 'space-around' },
    compCol: { alignItems: 'center', gap: 2 },
    compActual: { color: colors.primary, fontSize: 30, fontWeight: '800' },
    compAnterior: { color: colors.textMuted, fontSize: 30, fontWeight: '800' },
    compLabel: { color: colors.textMuted, fontSize: 12 },
    pill: { textAlign: 'center', fontSize: 14, fontWeight: '700' },
    metaLine: { color: colors.textMuted, fontSize: 13, textAlign: 'center' },
    plot: { position: 'relative', justifyContent: 'flex-end' },
    metaBar: { position: 'absolute', left: 0, right: 0, height: 1, backgroundColor: colors.accent },
    barsRow: { flexDirection: 'row', alignItems: 'flex-end', height: '100%', gap: 2 },
    barPair: { flex: 1, flexDirection: 'row', justifyContent: 'center', alignItems: 'flex-end', gap: 2 },
    barActual: { width: 8, backgroundColor: colors.primary, borderTopLeftRadius: 3, borderTopRightRadius: 3 },
    barAnterior: { width: 6, backgroundColor: colors.textMuted, borderTopLeftRadius: 3, borderTopRightRadius: 3, opacity: 0.5 },
    labelsRow: { flexDirection: 'row', gap: 2 },
    barLabel: { flex: 1, textAlign: 'center', color: colors.textMuted, fontSize: 10 },
    legend: { flexDirection: 'row', justifyContent: 'center', gap: spacing.md, marginTop: spacing.xs },
    legendItem: { flexDirection: 'row', alignItems: 'center', gap: 4 },
    legendDot: { width: 10, height: 10, borderRadius: 5 },
    legendText: { color: colors.textMuted, fontSize: 12 },
    macroRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
    macroLabel: { color: colors.text, fontSize: 13, width: 70 },
    macroBarBg: { flex: 1, height: 10, borderRadius: 5, backgroundColor: colors.surfaceAlt, overflow: 'hidden' },
    macroBarFill: { height: '100%', borderRadius: 5 },
    macroVal: { color: colors.textMuted, fontSize: 12, width: 80, textAlign: 'right' },
    pressed: { opacity: 0.7 },
  });
```

- [ ] **Step 2: Verificar tipos**

Run (desde `frontend/`): `.\node_modules\.bin\tsc --noEmit`
Expected: sin errores.

- [ ] **Step 3: Commit**

```bash
git -C frontend add src/screens/DashboardScreen.tsx
git -C frontend commit -m "feat(dashboard): pantalla comparativa (layout B) con barras en View"
```

---

### Task 7: Tercer tab "Dashboard" en la cabecera

**Files:**
- Modify: `frontend/App.tsx`

- [ ] **Step 1: Importar la pantalla**

En `frontend/App.tsx`, debajo de `import PerfilScreen from './src/screens/PerfilScreen';` añadir:

```typescript
import DashboardScreen from './src/screens/DashboardScreen';
```

- [ ] **Step 2: Ampliar el tipo de tab**

Cambiar la línea `type Tab = 'captura' | 'historial';` por:

```typescript
type Tab = 'captura' | 'historial' | 'dashboard';
```

- [ ] **Step 3: Añadir el tab en la cabecera**

En el bloque `<View style={styles.navTabs}>`, después del `TabButton` de "Historial", añadir:

```tsx
                  <TabButton
                    label="Dashboard"
                    icon="barChart"
                    active={tab === 'dashboard'}
                    onPress={() => setTab('dashboard')}
                  />
```

- [ ] **Step 4: Renderizar la pantalla**

En el `<View style={styles.body}>`, cambiar el bloque de selección de pantalla por uno con los tres casos:

```tsx
              <View style={styles.body}>
                {tab === 'captura' ? (
                  <CaptureScreen auth={auth} />
                ) : tab === 'historial' ? (
                  <HistoryScreen auth={auth} />
                ) : (
                  <DashboardScreen auth={auth} />
                )}
              </View>
```

- [ ] **Step 5: Verificar tipos**

Run (desde `frontend/`): `.\node_modules\.bin\tsc --noEmit`
Expected: sin errores.

- [ ] **Step 6: Verificación manual por web**

Arrancar backend (con `GOOGLE_APPLICATION_CREDENTIALS`) y frontend (`npm run web`). Login →
tab **Dashboard** → cambiar de período (Día/Semana/Mes/…) → ver las dos cifras (actual vs
anterior), el % de cambio, las barras (actual vs anterior + línea de meta) y los macros vs meta.
Con datos del día (capturar comidas en Captura) el período "Día" muestra buckets por comida.

- [ ] **Step 7: Commit**

```bash
git -C frontend add App.tsx
git -C frontend commit -m "feat(nav): tercer tab Dashboard"
```

---

## Verificación final de la Fase C

- [ ] `dotnet test "backend/tests/Calorias.Tests/Calorias.Tests.csproj"` → PASS (Fase A + B + RangosPeriodo + ServicioResumenPeriodos).
- [ ] `frontend/.\node_modules\.bin\tsc --noEmit` → sin errores.
- [ ] `GET /api/comidas/resumen?periodo=semanal` sin token → 401.
- [ ] Flujo manual: con comidas capturadas, el Dashboard muestra actual vs anterior, % de cambio,
  barras con línea de meta y macros vs meta; cambiar de período recalcula; el período "Día"
  bucketiza por comida.

## Notas de diseño (no implementar de más)
- `promedioKcalDia` es media sobre **días con registro** (no días calendario), para reflejar la
  ingesta típica de los días en que el usuario registró.
- La meta es la **actual** del perfil (se aplica igual a "actual" y "anterior"); si el perfil está
  incompleto, `meta` y `deficitMedioVsMeta` van en null y el dashboard lo indica.
- Sin librería de charts: las barras y la línea de meta se dibujan con `View`s (spec, sección 7).
