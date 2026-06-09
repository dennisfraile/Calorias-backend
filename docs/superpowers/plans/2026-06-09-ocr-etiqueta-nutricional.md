# OCR de etiqueta nutricional Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Registrar productos envasados escaneando su etiqueta nutricional: Gemini multimodal (free tier) lee la tabla (imagen→JSON), el usuario indica porciones, y se guarda como una comida.

**Architecture:** Segundo pipeline paralelo al de "plato" (no usa USDA). `ServicioEtiquetaGemini` (HttpClient a Google AI Studio) extrae una `EtiquetaNutricional`; un builder puro `RegistroDesdeEtiqueta` la escala por porciones a un `RegistroComida` de 1 detalle. Dos endpoints (`leer-etiqueta` sin guardar, `guardar-etiqueta`). Frontend: toggle plato/etiqueta + flujo de 2 pasos. Sin migración de BD.

**Tech Stack:** .NET 10, xUnit (`Assert.*`), EF Core 10, HttpClient tipado, Google Gemini API (REST, `gemini-2.0-flash`, free tier, API key en user-secrets `Gemini:ApiKey`). Frontend Expo SDK 54 / RN 0.81 / TS.

**Arranque/parada backend:** desde `backend/src/Calorias.Api` con `$env:ASPNETCORE_ENVIRONMENT='Development'` + `$env:GOOGLE_APPLICATION_CREDENTIALS=...`; `dotnet run --launch-profile http`. Tests: `dotnet test` desde DENTRO de `backend/tests/Calorias.Tests` (el path de `backend` tiene espacio y no hay .sln; un `dotnet test` en `backend` solo restaura). tsc frontend: `frontend\.\node_modules\.bin\tsc --noEmit` (NO npx).

**Repos:** `backend` (main) y `frontend` (master). Rama nueva por repo, PR por la web.

---

### Task 1: Núcleo de dominio — `EtiquetaNutricional`, abstracción y builder puro

**Files:**
- Create: `backend/src/Calorias.Application/Abstractions/IServicioEtiquetaNutricional.cs`
- Create: `backend/src/Calorias.Application/Servicios/RegistroDesdeEtiqueta.cs`
- Test: `backend/tests/Calorias.Tests/RegistroDesdeEtiquetaTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Calorias.Domain.Entities;
using Xunit;

namespace Calorias.Tests;

public class RegistroDesdeEtiquetaTests
{
    private static EtiquetaNutricional Refresco() => new(
        NombreProducto: "Kolashanpan",
        TamPorcion: 250m, UnidadPorcion: "mL", PorcionesPorEnvase: 1.4m,
        CaloriasPorPorcion: 84m, ProteinaPorPorcion: 0m, CarbosPorPorcion: 21m, GrasasPorPorcion: 0m);

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
        Assert.Equal(reg.Detalles[0].Calorias, reg.CaloriasTotales);
    }

    [Fact]
    public void Nombre_vacio_cae_a_Producto()
    {
        var etq = Refresco() with { NombreProducto = null };
        var reg = RegistroDesdeEtiqueta.Construir(etq, 1m, "u1", TipoComida.Cena, new System.DateOnly(2026, 6, 9));
        Assert.Equal("Producto", reg.Detalles[0].NombreAlimento);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

From `backend/tests/Calorias.Tests`: `dotnet test --filter RegistroDesdeEtiquetaTests`
Expected: compile FAIL (`EtiquetaNutricional` / `RegistroDesdeEtiqueta` no existen).

- [ ] **Step 3: Implement the abstraction + record** — `IServicioEtiquetaNutricional.cs`

```csharp
namespace Calorias.Application.Abstractions;

/// <summary>Datos leídos de una etiqueta nutricional (valores POR PORCIÓN).</summary>
public record EtiquetaNutricional(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal? PorcionesPorEnvase,
    decimal CaloriasPorPorcion,
    decimal ProteinaPorPorcion,
    decimal CarbosPorPorcion,
    decimal GrasasPorPorcion);

public interface IServicioEtiquetaNutricional
{
    /// <summary>Lee la etiqueta de una imagen. Devuelve null si no logra leer una etiqueta legible.</summary>
    Task<EtiquetaNutricional?> LeerEtiquetaAsync(
        Stream imagen, string mimeType, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement the pure builder** — `RegistroDesdeEtiqueta.cs`

```csharp
using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;

namespace Calorias.Application.Servicios;

/// <summary>
/// Construye un RegistroComida (1 detalle) a partir de una EtiquetaNutricional escalada
/// por el número de porciones consumidas. Puro: sin BD ni dependencias.
/// </summary>
public static class RegistroDesdeEtiqueta
{
    public static RegistroComida Construir(
        EtiquetaNutricional etq, decimal porciones, string usuarioId,
        TipoComida tipo, DateOnly fechaLocal)
    {
        var detalle = new DetalleComida
        {
            NombreAlimento = string.IsNullOrWhiteSpace(etq.NombreProducto) ? "Producto" : etq.NombreProducto!,
            Cantidad = etq.TamPorcion * porciones,
            UnidadMedida = etq.UnidadPorcion,
            Calorias = etq.CaloriasPorPorcion * porciones,
            Proteinas = etq.ProteinaPorPorcion * porciones,
            Carbohidratos = etq.CarbosPorPorcion * porciones,
            Grasas = etq.GrasasPorPorcion * porciones,
            ConfianzaDeteccion = 1d,
        };

        var registro = new RegistroComida
        {
            UsuarioId = usuarioId,
            Tipo = tipo,
            FechaLocal = fechaLocal,
            Detalles = new List<DetalleComida> { detalle },
            CaloriasTotales = detalle.Calorias,
            ProteinasTotales = detalle.Proteinas,
            CarbohidratosTotales = detalle.Carbohidratos,
            GrasasTotales = detalle.Grasas,
        };
        return registro;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

From `backend/tests/Calorias.Tests`: `dotnet test --filter RegistroDesdeEtiquetaTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/backend" add src/Calorias.Application/Abstractions/IServicioEtiquetaNutricional.cs src/Calorias.Application/Servicios/RegistroDesdeEtiqueta.cs tests/Calorias.Tests/RegistroDesdeEtiquetaTests.cs
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/backend" commit -m "feat(etiqueta): EtiquetaNutricional + builder puro RegistroDesdeEtiqueta"
```

---

### Task 2: `ServicioEtiquetaGemini` (Infrastructure) + DI + config

**Files:**
- Create: `backend/src/Calorias.Infrastructure/Servicios/ServicioEtiquetaGemini.cs`
- Modify: `backend/src/Calorias.Infrastructure/DependencyInjection.cs`

Sin unit test (adapter HTTP); se verifica por build + E2E real. Usa la API REST de Gemini (Google AI Studio).

- [ ] **Step 1: Implement `ServicioEtiquetaGemini.cs`**

```csharp
using System.Text;
using System.Text.Json;
using Calorias.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Lee una etiqueta nutricional con Gemini (Google AI Studio, free tier) vía REST:
/// envía la imagen inline + un prompt que pide JSON estructurado (responseSchema) con los
/// valores POR PORCIÓN ya en kcal. Best-effort: ante cualquier fallo devuelve null.
/// API key en config 'Gemini:ApiKey'. Modelo: gemini-2.0-flash.
/// </summary>
public class ServicioEtiquetaGemini(HttpClient http, IConfiguration config) : IServicioEtiquetaNutricional
{
    private const string Modelo = "gemini-2.0-flash";

    private const string Prompt =
        "Eres un extractor de etiquetas nutricionales. Lee la tabla nutricional de la imagen y " +
        "devuelve SOLO los datos POR PORCIÓN. Convierte la energía a kcal (si está en kJ, divídela " +
        "entre 4.184). 'tamPorcion' es el tamaño de una porción y 'unidadPorcion' su unidad ('g' o 'mL'). " +
        "Incluye 'porcionesPorEnvase' si aparece. Si la imagen NO es una etiqueta nutricional legible, " +
        "pon 0 en caloriasPorPorcion. Responde en el idioma del producto para 'nombreProducto'.";

    public async Task<EtiquetaNutricional?> LeerEtiquetaAsync(
        Stream imagen, string mimeType, CancellationToken ct = default)
    {
        try
        {
            using var ms = new MemoryStream();
            await imagen.CopyToAsync(ms, ct);
            var base64 = Convert.ToBase64String(ms.ToArray());

            var apiKey = config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey)) return null;

            var cuerpo = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = Prompt },
                            new { inlineData = new { mimeType, data = base64 } },
                        },
                    },
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            nombreProducto = new { type = "STRING", nullable = true },
                            tamPorcion = new { type = "NUMBER" },
                            unidadPorcion = new { type = "STRING" },
                            porcionesPorEnvase = new { type = "NUMBER", nullable = true },
                            caloriasPorPorcion = new { type = "NUMBER" },
                            proteinaPorPorcion = new { type = "NUMBER" },
                            carbosPorPorcion = new { type = "NUMBER" },
                            grasasPorPorcion = new { type = "NUMBER" },
                        },
                        required = new[]
                        {
                            "tamPorcion", "unidadPorcion", "caloriasPorPorcion",
                            "proteinaPorPorcion", "carbosPorPorcion", "grasasPorPorcion",
                        },
                    },
                },
            };

            var json = JsonSerializer.Serialize(cuerpo);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"models/{Modelo}:generateContent?key={Uri.EscapeDataString(apiKey)}";

            using var resp = await http.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var respJson = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(respJson);

            // candidates[0].content.parts[0].text contiene el JSON pedido (responseMimeType=application/json).
            if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
                return null;
            var texto = cands[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(texto)) return null;

            using var datos = JsonDocument.Parse(texto);
            var r = datos.RootElement;

            decimal Num(string prop) =>
                r.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number
                    ? el.GetDecimal() : 0m;
            decimal? NumNull(string prop) =>
                r.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number
                    ? el.GetDecimal() : (decimal?)null;
            string? Str(string prop) =>
                r.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() : null;

            var calorias = Num("caloriasPorPorcion");
            if (calorias <= 0m) return null; // no es etiqueta legible

            return new EtiquetaNutricional(
                NombreProducto: Str("nombreProducto"),
                TamPorcion: Num("tamPorcion") > 0m ? Num("tamPorcion") : 100m,
                UnidadPorcion: Str("unidadPorcion") ?? "g",
                PorcionesPorEnvase: NumNull("porcionesPorEnvase"),
                CaloriasPorPorcion: calorias,
                ProteinaPorPorcion: Num("proteinaPorPorcion"),
                CarbosPorPorcion: Num("carbosPorPorcion"),
                GrasasPorPorcion: Num("grasasPorPorcion"));
        }
        catch
        {
            return null;
        }
    }
}
```

> Nota de verificación (en el E2E): confirmar que el endpoint REST v1beta acepta `inlineData`/`mimeType`
> camelCase y `generationConfig.responseSchema` con el modelo `gemini-2.0-flash` del free tier. Si el
> nombre del modelo o un campo difiere, ajustar (`Modelo` y los nombres del cuerpo). El servicio es
> best-effort: ante error devuelve null y el modo plato sigue intacto.

- [ ] **Step 2: Registrar el HttpClient tipado en DI**

En `DependencyInjection.cs`, dentro de `AddInfrastructure`, junto a los otros `AddHttpClient` (debajo del de USDA), añadir:

```csharp
        // Gemini (Google AI Studio, free tier) para leer etiquetas nutricionales.
        services.AddHttpClient<IServicioEtiquetaNutricional, ServicioEtiquetaGemini>(c =>
        {
            c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
            c.Timeout = TimeSpan.FromSeconds(30);
        });
```

(El `using Calorias.Application.Abstractions;` ya está en el archivo; `ServicioEtiquetaGemini` está en `Calorias.Infrastructure.Servicios`, también ya importado.)

- [ ] **Step 3: Build (API parada)**

From `backend/tests/Calorias.Tests`: `dotnet test` (compila Infrastructure + corre la suite).
Expected: build OK, todos los tests verdes (los 3 de Task 1 + los previos).

- [ ] **Step 4: Commit**

```bash
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/backend" add src/Calorias.Infrastructure/Servicios/ServicioEtiquetaGemini.cs src/Calorias.Infrastructure/DependencyInjection.cs
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/backend" commit -m "feat(etiqueta): ServicioEtiquetaGemini (Gemini multimodal) + DI"
```

---

### Task 3: DTOs + endpoints `leer-etiqueta` y `guardar-etiqueta`

**Files:**
- Create: `backend/src/Calorias.Api/Dtos/EtiquetaDtos.cs`
- Modify: `backend/src/Calorias.Api/Controllers/ComidasController.cs`

- [ ] **Step 1: Crear los DTOs** — `EtiquetaDtos.cs`

```csharp
namespace Calorias.Api.Dtos;

/// <summary>Respuesta de POST /api/comidas/leer-etiqueta (previsualización, no guarda).</summary>
public record EtiquetaNutricionalDto(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal? PorcionesPorEnvase,
    decimal CaloriasPorPorcion,
    decimal ProteinaPorPorcion,
    decimal CarbosPorPorcion,
    decimal GrasasPorPorcion);

/// <summary>Cuerpo de POST /api/comidas/guardar-etiqueta.</summary>
public record GuardarEtiquetaDto(
    string? NombreProducto,
    decimal TamPorcion,
    string UnidadPorcion,
    decimal CaloriasPorPorcion,
    decimal ProteinaPorPorcion,
    decimal CarbosPorPorcion,
    decimal GrasasPorPorcion,
    decimal Porciones,
    string Tipo,
    DateOnly FechaLocal);
```

- [ ] **Step 2: Añadir la dependencia al controller**

En `ComidasController.cs`, añadir `IServicioEtiquetaNutricional etiquetas` al constructor primario (junto a las otras deps, antes de `CaloriasDbContext db`):

```csharp
public class ComidasController(
    OrquestadorAnalisisComida orquestador,
    IServicioUsuarios usuarios,
    IServicioResumenDiario resumen,
    IServicioResumenPeriodos resumenPeriodos,
    IServicioCorreccionPorciones correccion,
    IServicioEtiquetaNutricional etiquetas,
    CaloriasDbContext db) : ControllerBase
```

(`IServicioEtiquetaNutricional` está en `Calorias.Application.Abstractions`, ya importado. `RegistroDesdeEtiqueta` está en `Calorias.Application.Servicios`; añadir `using Calorias.Application.Servicios;` al inicio si no está — sí lo usa `EtiquetaNutricional`/builder.)

- [ ] **Step 3: Añadir los dos endpoints tras `CorregirPorciones`**

```csharp
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
            etq.CaloriasPorPorcion, etq.ProteinaPorPorcion, etq.CarbosPorPorcion, etq.GrasasPorPorcion));
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

        var etq = new EtiquetaNutricional(
            cuerpo.NombreProducto, cuerpo.TamPorcion, cuerpo.UnidadPorcion, null,
            cuerpo.CaloriasPorPorcion, cuerpo.ProteinaPorPorcion, cuerpo.CarbosPorPorcion, cuerpo.GrasasPorPorcion);
        var registro = RegistroDesdeEtiqueta.Construir(etq, cuerpo.Porciones, usuario.Id, tipo, cuerpo.FechaLocal);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Registros.Add(registro);
        await db.SaveChangesAsync(ct);
        await resumen.RecalcularDiaAsync(usuario.Id, cuerpo.FechaLocal, ct);
        await tx.CommitAsync(ct);

        return Ok(ADto(registro));
    }
```

(Reusa el helper privado `ADto(RegistroComida)` existente y `AsegurarUsuarioAsync` como en `Analizar`.)

- [ ] **Step 4: Build + tests**

From `backend/tests/Calorias.Tests`: `dotnet test`.
Expected: build OK, todos verdes. Además compila `Calorias.Api`: `dotnet build "c:/Users/LENOVO/Documents/Portafolio/App Calorias/backend/src/Calorias.Api"` → OK.

- [ ] **Step 5: Commit**

```bash
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/backend" add src/Calorias.Api/
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/backend" commit -m "feat(etiqueta): endpoints leer-etiqueta y guardar-etiqueta"
```

---

### Task 4: Frontend — cliente API de etiqueta

**Files:**
- Modify: `frontend/src/api/comidas.ts`

- [ ] **Step 1: Añadir tipos y funciones al final de `comidas.ts`**

```typescript
export interface EtiquetaNutricional {
  nombreProducto?: string;
  tamPorcion: number;
  unidadPorcion: string;
  porcionesPorEnvase?: number | null;
  caloriasPorPorcion: number;
  proteinaPorPorcion: number;
  carbosPorPorcion: number;
  grasasPorPorcion: number;
}

/** Lee una etiqueta nutricional (no guarda). */
export async function leerEtiqueta(fotoUri: string, idToken: string): Promise<EtiquetaNutricional> {
  const nombre = fotoUri.split('/').pop() || `etiqueta-${Date.now()}.jpg`;
  const form = new FormData();
  if (Platform.OS === 'web') {
    const blob = await (await fetch(fotoUri)).blob();
    form.append('foto', blob, nombre);
  } else {
    form.append('foto', { uri: fotoUri, name: nombre, type: inferirMime(nombre) } as unknown as Blob);
  }

  const url = `${API_BASE_URL}/api/comidas/leer-etiqueta`;
  let res: Response;
  try {
    res = await fetch(url, {
      method: 'POST',
      headers: { Authorization: `Bearer ${idToken}`, Accept: 'application/json' },
      body: form,
    });
  } catch (e) {
    throw new ApiError(`No se pudo conectar con el backend (${url}). ${e instanceof Error ? e.message : ''}`, 0);
  }
  if (!res.ok) {
    const detalle = await res.text().catch(() => '');
    if (res.status === 401) throw new ApiError('No autorizado (401).', 401);
    throw new ApiError(detalle.slice(0, 300) || `El backend respondió ${res.status}.`, res.status);
  }
  return (await res.json()) as EtiquetaNutricional;
}

export interface GuardarEtiquetaPayload extends EtiquetaNutricional {
  porciones: number;
  tipo: TipoComida;
  fechaLocal: string;
}

/** Guarda una comida desde los datos de una etiqueta + porciones. */
export async function guardarEtiqueta(
  payload: GuardarEtiquetaPayload,
  idToken: string,
): Promise<RegistroComida> {
  const url = `${API_BASE_URL}/api/comidas/guardar-etiqueta`;
  let res: Response;
  try {
    res = await fetch(url, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${idToken}`,
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(payload),
    });
  } catch (e) {
    throw new ApiError(`No se pudo conectar con el backend (${url}). ${e instanceof Error ? e.message : ''}`, 0);
  }
  if (!res.ok) {
    const detalle = await res.text().catch(() => '');
    if (res.status === 401) throw new ApiError('No autorizado (401).', 401);
    throw new ApiError(detalle.slice(0, 300) || `El backend respondió ${res.status}.`, res.status);
  }
  return (await res.json()) as RegistroComida;
}
```

(`API_BASE_URL`, `Platform`, `ApiError`, `inferirMime`, `RegistroComida`, `TipoComida` ya existen/están importados en `comidas.ts` por las funciones previas — confirmar el import de `API_BASE_URL` desde `../config`, añadido en la feature de corrección.)

- [ ] **Step 2: Verificar tipos**

From `frontend`: `.\node_modules\.bin\tsc --noEmit`
Expected: 0 errores.

- [ ] **Step 3: Commit**

```bash
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/frontend" add src/api/comidas.ts
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/frontend" commit -m "feat(etiqueta): cliente leerEtiqueta + guardarEtiqueta"
```

---

### Task 5: Frontend — toggle de modo + flujo de etiqueta en `CaptureScreen`

**Files:**
- Modify: `frontend/src/screens/CaptureScreen.tsx`

El modo "plato" actual queda intacto; se añade un toggle y, en modo "etiqueta", el flujo de 2 pasos.

- [ ] **Step 1: Imports**

En el import de `../api/comidas`, añadir `leerEtiqueta`, `guardarEtiqueta`, y los tipos:

```typescript
import {
  analizarFoto,
  actualizarPorciones,
  leerEtiqueta,
  guardarEtiqueta,
  ApiError,
  type CorreccionPorcion,
  type DetalleComida,
  type EtiquetaNutricional,
  type RegistroComida,
  type TipoComida,
} from '../api/comidas';
```

Confirmar que `TextInput` ya está importado de `react-native` (se añadió en la feature de corrección).

- [ ] **Step 2: Estado del modo y de la etiqueta**

Dentro de `CaptureScreen`, junto a los `useState` existentes, añadir:

```typescript
  const [modo, setModo] = useState<'plato' | 'etiqueta'>('plato');
  const [etiqueta, setEtiqueta] = useState<EtiquetaNutricional | null>(null);
  const [porciones, setPorciones] = useState<string>('1');
  const [leyendo, setLeyendo] = useState(false);
  const [guardandoEtq, setGuardandoEtq] = useState(false);
```

- [ ] **Step 3: Toggle de modo (arriba del todo, dentro del primer card o sobre el de "Tipo de comida")**

Justo después del `<View style={styles.head}>...</View>`, insertar:

```tsx
        <View style={styles.modoRow}>
          {(['plato', 'etiqueta'] as const).map((m) => (
            <Pressable
              key={m}
              onPress={() => {
                setModo(m);
                setResultado(null);
                setEtiqueta(null);
              }}
              style={({ pressed }) => [styles.modoChip, modo === m && styles.modoChipActive, pressed && styles.pressed]}
            >
              <Text style={[styles.modoChipText, modo === m && styles.modoChipTextActive]}>
                {m === 'plato' ? 'Analizar plato' : 'Escanear etiqueta'}
              </Text>
            </Pressable>
          ))}
        </View>
```

- [ ] **Step 4: Acciones de etiqueta**

Añadir dentro de `CaptureScreen` (junto a `analizar`):

```typescript
  const leer = async () => {
    if (!fotoUri || !auth.idToken) return;
    setLeyendo(true);
    setError(null);
    setEtiqueta(null);
    try {
      const etq = await leerEtiqueta(fotoUri, auth.idToken);
      setEtiqueta(etq);
      setPorciones(etq.porcionesPorEnvase ? String(etq.porcionesPorEnvase) : '1');
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'No se pudo leer la etiqueta.');
    } finally {
      setLeyendo(false);
    }
  };

  const guardarDeEtiqueta = async () => {
    if (!etiqueta || !auth.idToken) return;
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
      const reg = await guardarEtiqueta(
        { ...etiqueta, porciones: n, tipo, fechaLocal },
        auth.idToken,
      );
      setResultado(reg);
      setEtiqueta(null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'No se pudo guardar.');
    } finally {
      setGuardandoEtq(false);
    }
  };
```

- [ ] **Step 5: Render condicional por modo**

Reemplazar el botón "Analizar foto" y el bloque de resultado por un render que dependa de `modo`. Concretamente, el botón de acción principal:

```tsx
        {modo === 'plato' ? (
          <Pressable
            onPress={analizar}
            disabled={analizarDeshabilitado}
            style={({ pressed }) => [
              styles.analyzeBtn,
              analizarDeshabilitado && styles.analyzeBtnDisabled,
              pressed && styles.pressed,
            ]}
          >
            {analizando ? (
              <ActivityIndicator color={colors.bg} />
            ) : (
              <View style={styles.analyzeInner}>
                <Icon name="zap" size={18} color={colors.bg} />
                <Text style={styles.analyzeBtnText}>Analizar foto</Text>
              </View>
            )}
          </Pressable>
        ) : (
          <Pressable
            onPress={leer}
            disabled={!fotoUri || leyendo || !auth.idToken}
            style={({ pressed }) => [
              styles.analyzeBtn,
              (!fotoUri || leyendo || !auth.idToken) && styles.analyzeBtnDisabled,
              pressed && styles.pressed,
            ]}
          >
            {leyendo ? (
              <ActivityIndicator color={colors.bg} />
            ) : (
              <View style={styles.analyzeInner}>
                <Icon name="zap" size={18} color={colors.bg} />
                <Text style={styles.analyzeBtnText}>Leer etiqueta</Text>
              </View>
            )}
          </Pressable>
        )}
```

Y donde hoy va `{resultado && <ResultadoCard .../>}`, dejarlo (sirve para ambos modos) y añadir, antes, la tarjeta de etiqueta:

```tsx
        {modo === 'etiqueta' && etiqueta && !resultado && (
          <View style={styles.card}>
            <Text style={styles.cardTitle}>{etiqueta.nombreProducto ?? 'Producto'}</Text>
            <Text style={styles.metaLineEtq}>
              Por porción ({etiqueta.tamPorcion} {etiqueta.unidadPorcion}): {Math.round(etiqueta.caloriasPorPorcion)} kcal ·
              P {etiqueta.proteinaPorPorcion} g · C {etiqueta.carbosPorPorcion} g · G {etiqueta.grasasPorPorcion} g
            </Text>
            {etiqueta.porcionesPorEnvase ? (
              <Text style={styles.metaLineEtq}>Porciones por envase: {etiqueta.porcionesPorEnvase}</Text>
            ) : null}
            <View style={styles.porcionesRow}>
              <Text style={styles.porcionesLabel}>¿Cuántas porciones?</Text>
              <TextInput
                value={porciones}
                onChangeText={setPorciones}
                keyboardType="decimal-pad"
                style={styles.porcionesInput}
              />
            </View>
            <Pressable
              onPress={guardarDeEtiqueta}
              disabled={guardandoEtq}
              style={({ pressed }) => [styles.guardarBtn, guardandoEtq && styles.analyzeBtnDisabled, pressed && styles.pressed]}
            >
              {guardandoEtq ? <ActivityIndicator color={colors.bg} /> : <Text style={styles.guardarBtnText}>Guardar</Text>}
            </Pressable>
          </View>
        )}
```

> `ResultadoCard` ya existe (de la feature de corrección) y muestra el `RegistroComida` guardado con
> edición de porciones; al guardar desde etiqueta seteamos `resultado`, así que se reusa tal cual.

- [ ] **Step 6: Estilos nuevos**

En `makeStyles`, añadir (reusa `radius`/`spacing`/`colors`; `guardarBtn`/`guardarBtnText`/`analyzeBtnDisabled`/`pressed`/`card`/`cardTitle` ya existen):

```typescript
  modoRow: { flexDirection: 'row', gap: spacing.xs },
  modoChip: {
    flex: 1, paddingVertical: spacing.sm, borderRadius: radius.md, alignItems: 'center',
    backgroundColor: colors.surfaceAlt, borderWidth: 1, borderColor: colors.border,
  },
  modoChipActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  modoChipText: { color: colors.textMuted, fontSize: 14, fontWeight: '700' },
  modoChipTextActive: { color: colors.bg },
  metaLineEtq: { color: colors.textMuted, fontSize: 13 },
  porcionesRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginTop: spacing.xs },
  porcionesLabel: { color: colors.text, fontSize: 14, fontWeight: '600' },
  porcionesInput: {
    minWidth: 60, textAlign: 'center', color: colors.text, fontSize: 15, fontWeight: '700',
    paddingVertical: 4, paddingHorizontal: 8, borderRadius: radius.sm,
    backgroundColor: colors.surfaceAlt, borderWidth: 1, borderColor: colors.border,
  },
```

- [ ] **Step 7: Verificar tipos**

From `frontend`: `.\node_modules\.bin\tsc --noEmit`
Expected: 0 errores.

- [ ] **Step 8: Commit**

```bash
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/frontend" add src/screens/CaptureScreen.tsx
git -C "c:/Users/LENOVO/Documents/Portafolio/App Calorias/frontend" commit -m "feat(captura): modo escanear etiqueta (toggle + flujo de 2 pasos)"
```

---

## Self-review / cierre

- [ ] `dotnet test` desde `backend/tests/Calorias.Tests` → todos verdes (incluye `RegistroDesdeEtiquetaTests`).
- [ ] `Calorias.Api` compila.
- [ ] `frontend\.\node_modules\.bin\tsc --noEmit` limpio.
- [ ] Prerrequisito E2E: API key gratis de Google AI Studio en user-secrets `Gemini:ApiKey`.
- [ ] Verificación E2E web: modo "Escanear etiqueta" → foto de una etiqueta → leer → ajustar porciones → guardar → aparece la comida; dashboard coherente.
- [ ] PRs por la web (backend → main, frontend → master) con `superpowers:finishing-a-development-branch`.

## Orden de ejecución

T1 (núcleo puro, TDD) → T2 (Gemini + DI) → T3 (endpoints) → T4 (cliente front) → T5 (UI). T4/T5 pueden ir tras T3.
