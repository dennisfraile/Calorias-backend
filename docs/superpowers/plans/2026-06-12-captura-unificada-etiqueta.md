# Captura unificada + edición de etiqueta — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fusionar los flujos de captura plato/etiqueta en una pantalla unificada, permitir editar todos los datos de una etiqueta antes de guardar, y soportar etiquetas expresadas por 100 g/mL además de por porción.

**Architecture:** El contrato de etiqueta gana una `base` (`porcion` | `cien`); los macros se almacenan "por base" y se exponen "por porción" mediante conversión pura. Backend (.NET, TDD sobre la lógica pura) propaga `base` por DTOs/Gemini/controller. Frontend (Expo) extrae el cálculo a un helper puro `etiquetaCalculos.ts` y unifica `CaptureScreen` con un segmented control y una tarjeta de etiqueta totalmente editable.

**Tech Stack:** .NET 10 (xUnit), EF Core/Npgsql, Gemini REST (gemini-2.5-flash-lite), Expo SDK 54 / React Native, TypeScript.

**Repos / ramas:** backend rama `feat/captura-unificada-etiqueta` (ya creada, contiene el spec). Frontend: crear rama `feat/captura-unificada-etiqueta` desde `master`. PR por la web en cada repo.

**Verificación global:**
- Backend tests DESDE `backend/tests/Calorias.Tests`: `dotnet test` (44 verdes hoy). Parar la API (puerto 5247) antes de build/test si está corriendo.
- Frontend tsc: `frontend/.\node_modules\.bin\tsc --noEmit` (NO `npx tsc`). No hay jest → la lógica pura se verifica por tsc + E2E manual en web.

---

## Parte 1 — Backend (.NET, TDD)

### Task 1: Base de medida en el dominio de etiqueta (pieza pura, TDD)

**Files:**
- Modify: `backend/src/Calorias.Application/Abstractions/IServicioEtiquetaNutricional.cs`
- Modify: `backend/tests/Calorias.Tests/RegistroDesdeEtiquetaTests.cs`
- (sin cambios) `backend/src/Calorias.Application/Servicios/RegistroDesdeEtiqueta.cs` — lee las propiedades `...PorPorcion` ya existentes (ahora calculadas); no se toca.

- [ ] **Step 1: Actualizar los tests existentes al nuevo constructor y añadir casos de base**

Reemplaza TODO el contenido de `backend/tests/Calorias.Tests/RegistroDesdeEtiquetaTests.cs` por:

```csharp
using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Calorias.Domain.Entities;
using Xunit;

namespace Calorias.Tests;

public class RegistroDesdeEtiquetaTests
{
    // Etiqueta POR PORCIÓN (base = Porcion): valores tal cual.
    private static EtiquetaNutricional Refresco() => new(
        NombreProducto: "Kolashanpan",
        TamPorcion: 250m, UnidadPorcion: "mL", PorcionesPorEnvase: 1.4m,
        Base: BaseMedida.Porcion,
        CaloriasPorBase: 84m, ProteinaPorBase: 0m, CarbosPorBase: 21m, GrasasPorBase: 0m);

    [Fact]
    public void Escala_macros_y_cantidad_por_porciones()
    {
        var reg = RegistroDesdeEtiqueta.Construir(
            Refresco(), porciones: 1.4m, usuarioId: "u1",
            tipo: TipoComida.Snack, fechaLocal: new System.DateOnly(2026, 6, 9));

        var d = Assert.Single(reg.Detalles);
        Assert.Equal("Kolashanpan", d.NombreAlimento);
        Assert.Equal(350m, d.Cantidad);            // 250 * 1.4
        Assert.Equal("mL", d.UnidadMedida);
        Assert.Equal(117.6m, d.Calorias);          // 84 * 1.4
        Assert.Equal(29.4m, d.Carbohidratos);      // 21 * 1.4
        Assert.Equal(0m, d.Proteinas);
        Assert.Equal(1d, d.ConfianzaDeteccion);
    }

    [Fact]
    public void Totales_del_registro_igualan_el_unico_detalle()
    {
        var reg = RegistroDesdeEtiqueta.Construir(
            Refresco(), 2m, "u1", TipoComida.Desayuno, new System.DateOnly(2026, 6, 9));

        Assert.Equal("u1", reg.UsuarioId);
        Assert.Equal(TipoComida.Desayuno, reg.Tipo);
        Assert.Equal(168m, reg.CaloriasTotales);   // 84 * 2
        Assert.Equal(42m, reg.CarbohidratosTotales);
        Assert.Equal(reg.Detalles.First().Calorias, reg.CaloriasTotales);
    }

    [Fact]
    public void Nombre_vacio_cae_a_Producto()
    {
        var etq = Refresco() with { NombreProducto = null };
        var reg = RegistroDesdeEtiqueta.Construir(etq, 1m, "u1", TipoComida.Cena, new System.DateOnly(2026, 6, 9));
        Assert.Equal("Producto", reg.Detalles.First().NombreAlimento);
    }

    [Fact]
    public void Base_cien_con_unidad_masa_convierte_a_porcion()
    {
        // Galleta: 500 kcal por 100 g, porción de 30 g => 150 kcal por porción.
        var etq = new EtiquetaNutricional(
            NombreProducto: "Galletas", TamPorcion: 30m, UnidadPorcion: "g", PorcionesPorEnvase: 8m,
            Base: BaseMedida.Cien,
            CaloriasPorBase: 500m, ProteinaPorBase: 10m, CarbosPorBase: 60m, GrasasPorBase: 25m);

        var reg = RegistroDesdeEtiqueta.Construir(etq, porciones: 2m, usuarioId: "u1",
            tipo: TipoComida.Snack, fechaLocal: new System.DateOnly(2026, 6, 12));

        var d = Assert.Single(reg.Detalles);
        Assert.Equal(60m, d.Cantidad);             // 30 g * 2 porciones
        Assert.Equal(300m, d.Calorias);            // (500 * 30/100) * 2 = 150 * 2
        Assert.Equal(6m, d.Proteinas);             // (10 * 0.3) * 2
        Assert.Equal(36m, d.Carbohidratos);        // (60 * 0.3) * 2
        Assert.Equal(15m, d.Grasas);               // (25 * 0.3) * 2
    }

    [Fact]
    public void Base_cien_con_unidad_no_masa_se_trata_como_porcion()
    {
        // Unidad "unidad" no permite la conversión por 100 => factor 1.
        var etq = new EtiquetaNutricional(
            NombreProducto: "Barra", TamPorcion: 1m, UnidadPorcion: "unidad", PorcionesPorEnvase: 6m,
            Base: BaseMedida.Cien,
            CaloriasPorBase: 90m, ProteinaPorBase: 2m, CarbosPorBase: 12m, GrasasPorBase: 3m);

        var reg = RegistroDesdeEtiqueta.Construir(etq, 1m, "u1", TipoComida.Snack, new System.DateOnly(2026, 6, 12));

        var d = Assert.Single(reg.Detalles);
        Assert.Equal(90m, d.Calorias);             // factor 1 (no es masa)
        Assert.Equal(2m, d.Proteinas);
    }
}
```

- [ ] **Step 2: Ejecutar los tests para verlos fallar (no compila)**

Run (desde `backend/tests/Calorias.Tests`): `dotnet test`
Expected: FAIL de compilación — `BaseMedida` no existe y `EtiquetaNutricional` no tiene `Base`/`...PorBase`.

- [ ] **Step 3: Reescribir el record con base + propiedades calculadas por porción**

Reemplaza TODO el contenido de `backend/src/Calorias.Application/Abstractions/IServicioEtiquetaNutricional.cs` por:

```csharp
namespace Calorias.Application.Abstractions;

/// <summary>Base en la que vienen expresados los macros de una etiqueta.</summary>
public enum BaseMedida { Porcion, Cien }

/// <summary>
/// Datos leídos de una etiqueta nutricional. Los macros se almacenan POR BASE
/// (<see cref="Base"/>) y se exponen POR PORCIÓN mediante las propiedades calculadas.
/// </summary>
public record EtiquetaNutricional(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal? PorcionesPorEnvase,
    BaseMedida Base,
    decimal CaloriasPorBase,
    decimal ProteinaPorBase,
    decimal CarbosPorBase,
    decimal GrasasPorBase)
{
    private bool UnidadEsMasa =>
        string.Equals(UnidadPorcion?.Trim(), "g", System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(UnidadPorcion?.Trim(), "ml", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Factor para pasar de "por base" a "por porción".</summary>
    private decimal FactorPorPorcion =>
        Base == BaseMedida.Cien && UnidadEsMasa && TamPorcion > 0m ? TamPorcion / 100m : 1m;

    public decimal CaloriasPorPorcion => CaloriasPorBase * FactorPorPorcion;
    public decimal ProteinaPorPorcion => ProteinaPorBase * FactorPorPorcion;
    public decimal CarbosPorPorcion => CarbosPorBase * FactorPorPorcion;
    public decimal GrasasPorPorcion => GrasasPorBase * FactorPorPorcion;
}

public interface IServicioEtiquetaNutricional
{
    /// <summary>Lee la etiqueta de una imagen. Devuelve null si no logra leer una etiqueta legible.</summary>
    System.Threading.Tasks.Task<EtiquetaNutricional?> LeerEtiquetaAsync(
        System.IO.Stream imagen, string mimeType, System.Threading.CancellationToken ct = default);
}
```

- [ ] **Step 4: Ejecutar los tests del record para verlos pasar**

Run (desde `backend/tests/Calorias.Tests`): `dotnet test --filter "FullyQualifiedName~RegistroDesdeEtiquetaTests"`
Expected: PASS los 5 tests. (El resto del backend aún NO compila: el controller y Gemini usan los nombres viejos — se arreglan en Tasks 3-4. Si `dotnet test` sin filtro falla a compilar, es esperado hasta Task 4.)

> Nota: `RegistroDesdeEtiqueta.Construir` NO se modifica — sigue leyendo `etq.CaloriasPorPorcion` etc., que ahora son las propiedades calculadas. La conversión vive en el record.

- [ ] **Step 5: Commit**

```bash
git add src/Calorias.Application/Abstractions/IServicioEtiquetaNutricional.cs tests/Calorias.Tests/RegistroDesdeEtiquetaTests.cs
git commit -m "feat(etiqueta): base de medida (porcion|cien) con conversion a por-porcion"
```

---

### Task 2: DTOs de etiqueta con `Base`

**Files:**
- Modify: `backend/src/Calorias.Api/Dtos/EtiquetaDtos.cs`

- [ ] **Step 1: Reescribir los DTOs**

Reemplaza TODO el contenido de `backend/src/Calorias.Api/Dtos/EtiquetaDtos.cs` por:

```csharp
namespace Calorias.Api.Dtos;

/// <summary>Respuesta de POST /api/comidas/leer-etiqueta (previsualización, no guarda).
/// Macros POR BASE; 'Base' = "porcion" | "cien".</summary>
public record EtiquetaNutricionalDto(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal? PorcionesPorEnvase,
    string Base,
    decimal CaloriasPorBase,
    decimal ProteinaPorBase,
    decimal CarbosPorBase,
    decimal GrasasPorBase);

/// <summary>Cuerpo de POST /api/comidas/guardar-etiqueta. Macros POR BASE.</summary>
public record GuardarEtiquetaDto(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    string Base,
    decimal CaloriasPorBase,
    decimal ProteinaPorBase,
    decimal CarbosPorBase,
    decimal GrasasPorBase,
    decimal Porciones,
    string Tipo,
    DateOnly FechaLocal);
```

- [ ] **Step 2: Commit (compila tras Task 3; este paso es solo el cambio de DTOs)**

```bash
git add src/Calorias.Api/Dtos/EtiquetaDtos.cs
git commit -m "feat(etiqueta): DTOs con base de medida y macros por base"
```

---

### Task 3: Controller propaga `Base`

**Files:**
- Modify: `backend/src/Calorias.Api/Controllers/ComidasController.cs` (métodos `LeerEtiqueta` y `GuardarEtiqueta`, ~líneas 158-201)

- [ ] **Step 1: Actualizar `LeerEtiqueta` para mapear el enum Base → string y emitir macros por base**

Reemplaza el bloque `return Ok(new EtiquetaNutricionalDto(...))` dentro de `LeerEtiqueta` por:

```csharp
        return Ok(new EtiquetaNutricionalDto(
            etq.NombreProducto, etq.TamPorcion, etq.UnidadPorcion, etq.PorcionesPorEnvase,
            etq.Base == BaseMedida.Cien ? "cien" : "porcion",
            etq.CaloriasPorBase, etq.ProteinaPorBase, etq.CarbosPorBase, etq.GrasasPorBase));
```

- [ ] **Step 2: Actualizar `GuardarEtiqueta` para parsear `Base` y construir la etiqueta por base**

Reemplaza la línea que construye `var etq = new EtiquetaNutricional(...)` dentro de `GuardarEtiqueta` por:

```csharp
        var baseMedida = string.Equals(cuerpo.Base?.Trim(), "cien", StringComparison.OrdinalIgnoreCase)
            ? BaseMedida.Cien : BaseMedida.Porcion;
        var etq = new EtiquetaNutricional(
            cuerpo.NombreProducto, cuerpo.TamPorcion, cuerpo.UnidadPorcion, null,
            baseMedida,
            cuerpo.CaloriasPorBase, cuerpo.ProteinaPorBase, cuerpo.CarbosPorBase, cuerpo.GrasasPorBase);
```

- [ ] **Step 3: Asegurar el `using`**

Verifica que el archivo tenga `using Calorias.Application.Abstractions;` en la cabecera (para `EtiquetaNutricional`/`BaseMedida`). Si no está, añádelo junto a los demás `using`.

- [ ] **Step 4: Commit**

```bash
git add src/Calorias.Api/Controllers/ComidasController.cs
git commit -m "feat(etiqueta): controller propaga base en leer/guardar"
```

---

### Task 4: Gemini lee la `base` real de la etiqueta

**Files:**
- Modify: `backend/src/Calorias.Infrastructure/Servicios/ServicioEtiquetaGemini.cs`

- [ ] **Step 1: Ampliar el prompt**

Reemplaza la constante `Prompt` por:

```csharp
    private const string Prompt =
        "Eres un extractor de etiquetas nutricionales. Lee la tabla nutricional de la imagen, " +
        "en CUALQUIER idioma. Devuelve los macros en la base que use la etiqueta: si la tabla los da " +
        "POR PORCIÓN usa base='porcion'; si los da POR 100 g/100 mL usa base='cien'. " +
        "'tamPorcion' es el tamaño de una porción y 'unidadPorcion' su unidad ('g' o 'mL'); cuando uses " +
        "base='cien', asegúrate de incluir 'tamPorcion' en g o mL. Convierte la energía a kcal (si está en " +
        "kJ, divídela entre 4.184). Incluye 'porcionesPorEnvase' si aparece. Si la imagen NO es una etiqueta " +
        "nutricional legible, pon 0 en caloriasPorBase. Responde en el idioma del producto para 'nombreProducto'.";
```

- [ ] **Step 2: Añadir `base` y renombrar los campos del `responseSchema` a `...PorBase`**

En `responseSchema.properties`, reemplaza las cuatro propiedades `caloriasPorPorcion`/`proteinaPorPorcion`/`carbosPorPorcion`/`grasasPorPorcion` y añade `base`:

```csharp
                        properties = new
                        {
                            nombreProducto = new { type = "STRING", nullable = true },
                            tamPorcion = new { type = "NUMBER" },
                            unidadPorcion = new { type = "STRING" },
                            porcionesPorEnvase = new { type = "NUMBER", nullable = true },
                            @base = new { type = "STRING", @enum = new[] { "porcion", "cien" } },
                            caloriasPorBase = new { type = "NUMBER" },
                            proteinaPorBase = new { type = "NUMBER" },
                            carbosPorBase = new { type = "NUMBER" },
                            grasasPorBase = new { type = "NUMBER" },
                        },
                        required = new[]
                        {
                            "tamPorcion", "unidadPorcion", "base", "caloriasPorBase",
                            "proteinaPorBase", "carbosPorBase", "grasasPorBase",
                        },
```

- [ ] **Step 3: Parsear `base` y los campos `...PorBase` en la respuesta**

Reemplaza el bloque que lee `var calorias = Num("caloriasPorPorcion");` … hasta el `return new EtiquetaNutricional(...)` por:

```csharp
            var calorias = Num("caloriasPorBase");
            if (calorias <= 0m)
            {
                logger.LogInformation("Gemini: imagen sin etiqueta legible (caloriasPorBase<=0).");
                return null; // no es etiqueta legible
            }

            var baseLeida = string.Equals(Str("base")?.Trim(), "cien", StringComparison.OrdinalIgnoreCase)
                ? BaseMedida.Cien : BaseMedida.Porcion;

            return new EtiquetaNutricional(
                NombreProducto: Str("nombreProducto"),
                TamPorcion: Num("tamPorcion") > 0m ? Num("tamPorcion") : 100m,
                UnidadPorcion: Str("unidadPorcion") ?? "g",
                PorcionesPorEnvase: NumNull("porcionesPorEnvase"),
                Base: baseLeida,
                CaloriasPorBase: calorias,
                ProteinaPorBase: Num("proteinaPorBase"),
                CarbosPorBase: Num("carbosPorBase"),
                GrasasPorBase: Num("grasasPorBase"));
```

- [ ] **Step 4: Asegurar `using System;`**

Verifica que el archivo tenga `using System;` (para `StringComparison`). Si no, añádelo.

- [ ] **Step 5: Build + suite completa verde**

Run (desde `backend/tests/Calorias.Tests`): `dotnet test`
Expected: compila y PASAN los 46 tests (44 previos + 2 nuevos de base). Si la API está corriendo en 5247, párala antes.

- [ ] **Step 6: Commit**

```bash
git add src/Calorias.Infrastructure/Servicios/ServicioEtiquetaGemini.cs
git commit -m "feat(etiqueta): Gemini detecta base (porcion|cien) y macros por base"
```

---

## Parte 2 — Frontend (Expo/TypeScript)

> Crear la rama antes de empezar: desde `frontend/`, `git checkout -b feat/captura-unificada-etiqueta`.

### Task 5: Helper puro `etiquetaCalculos.ts`

**Files:**
- Create: `frontend/src/screens/etiquetaCalculos.ts`

- [ ] **Step 1: Crear el helper**

Crea `frontend/src/screens/etiquetaCalculos.ts` con:

```typescript
import type { EtiquetaNutricional, BaseEtiqueta } from '../api/comidas';

const esMasa = (unidad: string): boolean =>
  ['g', 'ml'].includes((unidad ?? '').trim().toLowerCase());

/** Factor para pasar de "por base" a "por porción". */
export function factorPorPorcion(base: BaseEtiqueta, tamPorcion: number, unidad: string): number {
  return base === 'cien' && esMasa(unidad) && tamPorcion > 0 ? tamPorcion / 100 : 1;
}

/** Valor de un macro por porción, aplicando la base. */
export function porPorcion(
  valorPorBase: number,
  base: BaseEtiqueta,
  tamPorcion: number,
  unidad: string,
): number {
  return valorPorBase * factorPorPorcion(base, tamPorcion, unidad);
}

export interface Macros {
  kcal: number;
  prot: number;
  carb: number;
  gra: number;
}

/** Totales consumidos = por-porción * nº de porciones. */
export function totalEtiqueta(etq: EtiquetaNutricional, porciones: number): Macros {
  const f = factorPorPorcion(etq.base, etq.tamPorcion, etq.unidadPorcion) * (porciones || 0);
  return {
    kcal: etq.caloriasPorBase * f,
    prot: etq.proteinaPorBase * f,
    carb: etq.carbosPorBase * f,
    gra: etq.grasasPorBase * f,
  };
}

/**
 * Re-expresa los 4 macros al cambiar de base, preservando el contenido real.
 * Solo convierte si la unidad es de masa y el tamaño es válido; si no, devuelve los
 * valores sin tocar (cambia solo la etiqueta de base).
 */
export function convertirMacros(
  valores: { caloriasPorBase: number; proteinaPorBase: number; carbosPorBase: number; grasasPorBase: number },
  deBase: BaseEtiqueta,
  aBase: BaseEtiqueta,
  tamPorcion: number,
  unidad: string,
): typeof valores {
  if (deBase === aBase || !esMasa(unidad) || tamPorcion <= 0) return valores;
  // porcion -> cien: x100/tam ; cien -> porcion: xtam/100
  const k = aBase === 'cien' ? 100 / tamPorcion : tamPorcion / 100;
  return {
    caloriasPorBase: valores.caloriasPorBase * k,
    proteinaPorBase: valores.proteinaPorBase * k,
    carbosPorBase: valores.carbosPorBase * k,
    grasasPorBase: valores.grasasPorBase * k,
  };
}
```

- [ ] **Step 2: tsc (fallará hasta Task 6 por los nuevos campos de `EtiquetaNutricional`)**

Run (desde `frontend/`): `.\node_modules\.bin\tsc --noEmit`
Expected: errores en `etiquetaCalculos.ts` por `base`/`...PorBase` aún no declarados en `comidas.ts`. Se resuelven en Task 6. (No commitear hasta que tsc esté limpio tras Task 6.)

---

### Task 6: Tipos del cliente con `base`

**Files:**
- Modify: `frontend/src/api/comidas.ts` (interface `EtiquetaNutricional`, ~líneas 164-173; `GuardarEtiquetaPayload` ~205-209)

- [ ] **Step 1: Reescribir la interface `EtiquetaNutricional`**

Reemplaza el bloque `export interface EtiquetaNutricional { ... }` por:

```typescript
export type BaseEtiqueta = 'porcion' | 'cien';

export interface EtiquetaNutricional {
  nombreProducto?: string;
  tamPorcion: number;
  unidadPorcion: string;
  porcionesPorEnvase?: number | null;
  base: BaseEtiqueta;
  caloriasPorBase: number;
  proteinaPorBase: number;
  carbosPorBase: number;
  grasasPorBase: number;
}
```

- [ ] **Step 2: tsc limpio**

Run (desde `frontend/`): `.\node_modules\.bin\tsc --noEmit`
Expected: errores SOLO en `CaptureScreen.tsx` (usa los campos viejos `caloriasPorPorcion`, etc.). `etiquetaCalculos.ts` ya compila. Se resuelven en Task 7.

- [ ] **Step 3: Commit (helper + tipos)**

```bash
git add src/screens/etiquetaCalculos.ts src/api/comidas.ts
git commit -m "feat(etiqueta): tipos con base + helper puro de calculo (etiquetaCalculos)"
```

---

### Task 7: Unificar `CaptureScreen` (segmented control + tarjeta editable + base + errores)

**Files:**
- Modify: `frontend/src/screens/CaptureScreen.tsx`

> Esta task reescribe el modo etiqueta. Conserva intacto el modo plato (`analizar` → `ResultadoCard`) y la estructura compartida (preview, tipo, botones de foto). El segmented control ya existe (`modoRow`); el cambio es: (a) un solo botón de acción que cambia de etiqueta, (b) la tarjeta de etiqueta pasa a ser totalmente editable con toggle de base y total en vivo, (c) estado de error/encuadre mejorado.

- [ ] **Step 1: Importar el helper y ampliar el estado editable de la etiqueta**

En el bloque de imports añade:

```typescript
import { convertirMacros, totalEtiqueta } from './etiquetaCalculos';
import type { BaseEtiqueta } from '../api/comidas';
```

Sustituye el estado `const [etiqueta, setEtiqueta] = useState<EtiquetaNutricional | null>(null);` por un estado editable derivado. Justo debajo de los `useState` existentes del modo etiqueta, añade un estado de borrador:

```typescript
  // Borrador editable de la etiqueta leída (todo editable antes de guardar).
  const [borrador, setBorrador] = useState<EtiquetaNutricional | null>(null);
```

(Conserva `porciones`, `leyendo`, `guardandoEtq`. Elimina el uso de `etiqueta`/`setEtiqueta`: el borrador lo reemplaza.)

- [ ] **Step 2: Al leer, inicializar el borrador**

Reemplaza el cuerpo de `leer` para volcar el resultado en el borrador:

```typescript
  const leer = async () => {
    if (!fotoUri || !auth.idToken) return;
    setLeyendo(true);
    setError(null);
    setBorrador(null);
    try {
      const etq = await leerEtiqueta(fotoUri, auth.idToken);
      setBorrador(etq);
      setPorciones(etq.porcionesPorEnvase ? String(etq.porcionesPorEnvase) : '1');
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'No se pudo leer la etiqueta.');
    } finally {
      setLeyendo(false);
    }
  };
```

- [ ] **Step 3: Reescribir `guardarDeEtiqueta` para enviar el borrador editado**

```typescript
  const guardarDeEtiqueta = async () => {
    if (!borrador || !auth.idToken) return;
    const n = parseFloat(porciones.replace(',', '.'));
    if (!Number.isFinite(n) || n <= 0) {
      setError('Indica un número de porciones válido.');
      return;
    }
    setGuardandoEtq(true);
    setError(null);
    try {
      const ahora = new Date();
      const fechaLocal = `${ahora.getFullYear()}-${String(ahora.getMonth() + 1).padStart(2, '0')}-${String(ahora.getDate()).padStart(2, '0')}`;
      const reg = await guardarEtiqueta({ ...borrador, porciones: n, tipo, fechaLocal }, auth.idToken);
      setResultado(reg);
      setBorrador(null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'No se pudo guardar.');
    } finally {
      setGuardandoEtq(false);
    }
  };
```

- [ ] **Step 4: Limpiar el borrador al cambiar de modo**

En el `onPress` del segmented control (`modoRow`), donde hoy hace `setEtiqueta(null)`, cámbialo a `setBorrador(null)`.

- [ ] **Step 5: Reemplazar la tarjeta de etiqueta de solo-lectura por la tarjeta editable**

Sustituye TODO el bloque `{modo === 'etiqueta' && etiqueta && !resultado && ( ... )}` por una tarjeta editable. Usa este JSX (define un sub-componente `EtiquetaEditor` al final del archivo para mantener el render principal legible):

```tsx
        {modo === 'etiqueta' && borrador && !resultado && (
          <EtiquetaEditor
            borrador={borrador}
            setBorrador={setBorrador}
            porciones={porciones}
            setPorciones={setPorciones}
            guardando={guardandoEtq}
            onGuardar={guardarDeEtiqueta}
          />
        )}
```

Y añade este componente al final del archivo (antes de `makeStyles`):

```tsx
function EtiquetaEditor({
  borrador,
  setBorrador,
  porciones,
  setPorciones,
  guardando,
  onGuardar,
}: {
  borrador: EtiquetaNutricional;
  setBorrador: (e: EtiquetaNutricional) => void;
  porciones: string;
  setPorciones: (s: string) => void;
  guardando: boolean;
  onGuardar: () => void;
}) {
  const { colors } = useTheme();
  const styles = makeStyles(colors);

  type CampoNum = 'tamPorcion' | 'caloriasPorBase' | 'proteinaPorBase' | 'carbosPorBase' | 'grasasPorBase';
  const setNum = (campo: CampoNum, txt: string) => {
    const v = parseFloat(txt.replace(',', '.'));
    setBorrador({ ...borrador, [campo]: Number.isFinite(v) ? v : 0 });
  };

  const cambiarBase = (nueva: BaseEtiqueta) => {
    if (nueva === borrador.base) return;
    const conv = convertirMacros(borrador, borrador.base, nueva, borrador.tamPorcion, borrador.unidadPorcion);
    setBorrador({ ...borrador, ...conv, base: nueva });
  };

  const n = parseFloat(porciones.replace(',', '.'));
  const total = totalEtiqueta(borrador, Number.isFinite(n) ? n : 0);

  return (
    <View style={styles.card}>
      <Text style={styles.cardTitle}>Datos de la etiqueta</Text>

      <View>
        <Text style={styles.editLabel}>Nombre</Text>
        <TextInput
          value={borrador.nombreProducto ?? ''}
          onChangeText={(t) => setBorrador({ ...borrador, nombreProducto: t })}
          placeholder="Producto"
          placeholderTextColor={colors.textMuted}
          style={styles.textField}
        />
      </View>

      <View style={styles.fieldRow}>
        <View style={styles.fieldCol}>
          <Text style={styles.editLabel}>Tam. porción</Text>
          <TextInput
            value={String(borrador.tamPorcion)}
            onChangeText={(t) => setNum('tamPorcion', t)}
            keyboardType="decimal-pad"
            style={styles.textField}
          />
        </View>
        <View style={styles.fieldCol}>
          <Text style={styles.editLabel}>Unidad</Text>
          <TextInput
            value={borrador.unidadPorcion}
            onChangeText={(t) => setBorrador({ ...borrador, unidadPorcion: t })}
            style={styles.textField}
          />
        </View>
      </View>

      <View>
        <Text style={styles.editLabel}>Macros</Text>
        <View style={styles.baseRow}>
          {(['porcion', 'cien'] as BaseEtiqueta[]).map((b) => (
            <Pressable
              key={b}
              onPress={() => cambiarBase(b)}
              style={({ pressed }) => [styles.baseChip, borrador.base === b && styles.baseChipActive, pressed && styles.pressed]}
            >
              <Text style={[styles.baseChipText, borrador.base === b && styles.baseChipTextActive]}>
                {b === 'porcion' ? 'por porción' : 'por 100 g'}
              </Text>
            </Pressable>
          ))}
        </View>
        <View style={styles.macroInputsRow}>
          <MacroInput label="kcal" value={borrador.caloriasPorBase} onChange={(t) => setNum('caloriasPorBase', t)} />
          <MacroInput label="P" value={borrador.proteinaPorBase} onChange={(t) => setNum('proteinaPorBase', t)} />
          <MacroInput label="C" value={borrador.carbosPorBase} onChange={(t) => setNum('carbosPorBase', t)} />
          <MacroInput label="G" value={borrador.grasasPorBase} onChange={(t) => setNum('grasasPorBase', t)} />
        </View>
      </View>

      <View style={styles.porcionesRow}>
        <Text style={styles.porcionesLabel}>Porciones</Text>
        <TextInput
          value={porciones}
          onChangeText={setPorciones}
          keyboardType="decimal-pad"
          style={styles.porcionesInput}
        />
      </View>

      <View style={styles.totalRow}>
        <Text style={styles.totalText}>
          Total: {Math.round(total.kcal)} kcal · P {Math.round(total.prot)} · C {Math.round(total.carb)} · G {Math.round(total.gra)}
        </Text>
      </View>

      <Pressable
        onPress={onGuardar}
        disabled={guardando}
        style={({ pressed }) => [styles.guardarBtn, guardando && styles.analyzeBtnDisabled, pressed && styles.pressed]}
      >
        {guardando ? <ActivityIndicator color={colors.bg} /> : <Text style={styles.guardarBtnText}>Guardar</Text>}
      </Pressable>
    </View>
  );
}

function MacroInput({ label, value, onChange }: { label: string; value: number; onChange: (t: string) => void }) {
  const { colors } = useTheme();
  const styles = makeStyles(colors);
  return (
    <View style={styles.macroInputCol}>
      <TextInput
        value={String(value)}
        onChangeText={onChange}
        keyboardType="decimal-pad"
        style={styles.macroInput}
      />
      <Text style={styles.macroInputLabel}>{label}</Text>
    </View>
  );
}
```

- [ ] **Step 6: Mejorar el estado de error con guía de encuadre + hint bajo el preview**

Reemplaza el bloque `{error && ( ... )}` por una tarjeta con guía cuando el error sea de lectura de etiqueta:

```tsx
        {error && (
          <View style={[styles.card, styles.errorCard]}>
            <Text style={styles.errorTitle}>{error}</Text>
            {modo === 'etiqueta' && (
              <View style={styles.guiaList}>
                <Text style={styles.guiaItem}>• Encuadra la tabla nutricional completa</Text>
                <Text style={styles.guiaItem}>• Que la foto esté enfocada y sin reflejos</Text>
                <Text style={styles.guiaItem}>• El texto derecho (no muy inclinado)</Text>
              </View>
            )}
          </View>
        )}
```

Y añade un hint debajo de la `actionsRow` (botones de foto), visible solo en modo etiqueta cuando aún no hay borrador:

```tsx
        {modo === 'etiqueta' && !borrador && (
          <Text style={styles.encuadreHint}>💡 Enfoca la tabla de información nutricional completa.</Text>
        )}
```

- [ ] **Step 7: Añadir los estilos nuevos**

En `makeStyles`, añade estas entradas al objeto `StyleSheet.create({ ... })`:

```typescript
  editLabel: { color: colors.textMuted, fontSize: 11, textTransform: 'uppercase', marginBottom: 4 },
  textField: {
    color: colors.text, fontSize: 15, paddingVertical: 8, paddingHorizontal: 10,
    borderRadius: radius.sm, backgroundColor: colors.surfaceAlt, borderWidth: 1, borderColor: colors.border,
  },
  fieldRow: { flexDirection: 'row', gap: spacing.sm },
  fieldCol: { flex: 1 },
  baseRow: { flexDirection: 'row', gap: spacing.xs, marginBottom: spacing.xs },
  baseChip: {
    paddingVertical: 6, paddingHorizontal: spacing.md, borderRadius: radius.md,
    backgroundColor: colors.surfaceAlt, borderWidth: 1, borderColor: colors.border,
  },
  baseChipActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  baseChipText: { color: colors.textMuted, fontSize: 12, fontWeight: '700' },
  baseChipTextActive: { color: colors.bg },
  macroInputsRow: { flexDirection: 'row', gap: spacing.xs },
  macroInputCol: { flex: 1, alignItems: 'center' },
  macroInput: {
    width: '100%', textAlign: 'center', color: colors.text, fontSize: 14, fontWeight: '700',
    paddingVertical: 6, borderRadius: radius.sm, backgroundColor: colors.surfaceAlt,
    borderWidth: 1, borderColor: colors.border,
  },
  macroInputLabel: { color: colors.textMuted, fontSize: 11, marginTop: 2 },
  totalRow: { paddingTop: spacing.xs },
  totalText: { color: colors.primary, fontSize: 14, fontWeight: '800' },
  errorTitle: { color: colors.danger, fontSize: 14, fontWeight: '700' },
  guiaList: { gap: 2, marginTop: spacing.xs },
  guiaItem: { color: colors.textMuted, fontSize: 13 },
  encuadreHint: { color: colors.textMuted, fontSize: 13, textAlign: 'center' },
```

- [ ] **Step 8: Verificar tsc limpio**

Run (desde `frontend/`): `.\node_modules\.bin\tsc --noEmit`
Expected: sin errores. (Si queda algún uso residual de `etiqueta`/`setEtiqueta`, eliminarlo.)

- [ ] **Step 9: Commit**

```bash
git add src/screens/CaptureScreen.tsx
git commit -m "feat(captura): pantalla unificada con etiqueta editable, base y guia de encuadre"
```

---

## Verificación final (manual, por el usuario)

- [ ] **Backend:** desde `backend/tests/Calorias.Tests`, `dotnet test` → 46 verdes.
- [ ] **Frontend:** desde `frontend/`, `.\node_modules\.bin\tsc --noEmit` → limpio.
- [ ] **E2E web** (`npm run web`, backend en 5247): 
  - Modo plato sin regresión (analizar → editar gramos → guardar).
  - Modo etiqueta por porción: leer → editar nombre/macros → total en vivo correcto → guardar → aparece en historial/dashboard.
  - Modo etiqueta por 100 g: cambiar el toggle de base recalcula coherentemente; guardar deja los kcal correctos.
  - Estado de error: foto que no es etiqueta → tarjeta con guía de encuadre.

## Entrega
PR por la web en backend (`feat/captura-unificada-etiqueta` → `main`) y frontend
(`feat/captura-unificada-etiqueta` → `master`). Merge por el usuario, sync local + borrar ramas.
Recordatorio git: remotos por SSH alias `github-dennis`; reintentar si el DNS falla.
```
