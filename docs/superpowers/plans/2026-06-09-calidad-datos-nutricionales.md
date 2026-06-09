# Calidad de datos nutricionales Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Subir la calidad de los datos del pipeline foto → Vision → USDA: filtrar etiquetas genéricas, elegir el mejor match USDA por relevancia, estimar porciones reales (con corrección manual), y mostrar los nombres en español.

**Architecture:** Clean Architecture (.NET 10). La lógica nueva testeable se aísla en piezas **puras** en `Domain`/`Application` (filtro, scoring, estimador, facade de traducción), inyectadas en `OrquestadorAnalisisComida` y `ServicioNutricionUsda`. La corrección manual añade un servicio con EF + endpoint PUT. El frontend (pantalla de captura, RN clásico) gana edición de porciones. Sin migración de BD (reescalado proporcional; el nombre ES se persiste en `NombreAlimento`).

**Tech Stack:** .NET 10, xUnit (`Assert.*`), EF Core 10 (InMemory en tests), Google.Cloud.Vision/Translation, USDA FoodData Central. Frontend: Expo SDK 54 / RN 0.81 / TypeScript.

**Arranque/parada backend:** desde `backend/src/Calorias.Api` con `$env:ASPNETCORE_ENVIRONMENT='Development'` y `$env:GOOGLE_APPLICATION_CREDENTIALS='C:\Users\LENOVO\secrets\calorias-498417-77abb89d3162.json'`, `dotnet run --launch-profile http`. **Parar la API antes de `dotnet build`/`test`** (bloquea DLLs). Tests: `dotnet test` desde `backend`. tsc frontend: `frontend\.\node_modules\.bin\tsc --noEmit`.

**Repos:** `backend` (default **main**) y `frontend` (default **master**). Rama nueva por repo, PR por la web.

---

## Parte A — Filtro de etiquetas (1A)

### Task A1: `FiltroEtiquetasComida` (puro, Application)

> **Corrección (arquitectura):** el filtro va en **Application**, no en Domain. `EtiquetaDetectada`
> vive en `Calorias.Application.Abstractions` y la dependencia es Application→Domain; un Domain que
> referencie Application sería circular. Se ubica junto a `OrquestadorAnalisisComida` (su consumidor).

**Files:**
- Create: `backend/src/Calorias.Application/Servicios/FiltroEtiquetasComida.cs`
- Test: `backend/tests/Calorias.Tests/FiltroEtiquetasComidaTests.cs`

- [ ] **Step 1: Escribir el test que falla**

```csharp
using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Xunit;

namespace Calorias.Tests;

public class FiltroEtiquetasComidaTests
{
    private static EtiquetaDetectada E(string d, float c = 0.9f) => new(d, c);

    [Fact]
    public void Descarta_etiquetas_genericas_no_comida()
    {
        var entrada = new[] { E("Food"), E("Dish"), E("Tableware"), E("Apple"), E("White rice") };
        var salida = FiltroEtiquetasComida.Filtrar(entrada);
        Assert.Equal(new[] { "Apple", "White rice" }, salida.Select(e => e.Descripcion).ToArray());
    }

    [Fact]
    public void Es_insensible_a_mayusculas()
    {
        var salida = FiltroEtiquetasComida.Filtrar(new[] { E("FOOD"), E("cuisine"), E("Banana") });
        Assert.Single(salida);
        Assert.Equal("Banana", salida[0].Descripcion);
    }

    [Fact]
    public void Deduplica_singular_plural_conservando_mayor_confianza()
    {
        var salida = FiltroEtiquetasComida.Filtrar(new[] { E("Apple", 0.7f), E("Apples", 0.95f) });
        Assert.Single(salida);
        Assert.Equal(0.95f, salida[0].Confianza);
    }

    [Fact]
    public void Si_todo_es_generico_devuelve_vacio()
    {
        var salida = FiltroEtiquetasComida.Filtrar(new[] { E("Food"), E("Meal"), E("Plate") });
        Assert.Empty(salida);
    }
}
```

- [ ] **Step 2: Correr el test para verlo fallar**

Run: `dotnet test backend --filter FiltroEtiquetasComidaTests`
Expected: FAIL de compilación ("FiltroEtiquetasComida no existe").

- [ ] **Step 3: Implementar `FiltroEtiquetasComida`**

```csharp
using Calorias.Application.Abstractions;

namespace Calorias.Application.Servicios;

/// <summary>
/// Quita de las etiquetas de Vision los términos genéricos/no-comida (denylist) y
/// deduplica por nombre normalizado (singular/plural), conservando la mayor confianza.
/// Puro: sin estado ni dependencias. Mantiene el orden de aparición.
/// </summary>
public static class FiltroEtiquetasComida
{
    private static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "food", "dish", "cuisine", "ingredient", "recipe", "produce", "meal", "tableware",
        "dishware", "plate", "bowl", "cutlery", "fork", "spoon", "knife", "drink", "drinkware",
        "breakfast", "lunch", "dinner", "brunch", "supper", "fast food", "junk food",
        "comfort food", "finger food", "whole food", "natural foods", "vegetarian food",
        "superfood", "staple food", "side dish", "garnish", "snack", "tablecloth", "tableware",
        "dessert", "baked goods", "cooking", "frozen food", "processed food", "leaf vegetable",
    };

    public static IReadOnlyList<EtiquetaDetectada> Filtrar(IReadOnlyList<EtiquetaDetectada> etiquetas)
    {
        var mejorPorClave = new Dictionary<string, EtiquetaDetectada>();
        var orden = new List<string>();

        foreach (var e in etiquetas)
        {
            var nombre = e.Descripcion.Trim();
            if (nombre.Length == 0 || Denylist.Contains(nombre)) continue;

            var clave = Normalizar(nombre);
            if (mejorPorClave.TryGetValue(clave, out var prev))
            {
                if (e.Confianza > prev.Confianza) mejorPorClave[clave] = e;
            }
            else
            {
                mejorPorClave[clave] = e;
                orden.Add(clave);
            }
        }

        return orden.Select(c => mejorPorClave[c]).ToList();
    }

    /// <summary>Minúsculas + quita una 's' final simple para unir singular/plural.</summary>
    private static string Normalizar(string s)
    {
        var x = s.Trim().ToLowerInvariant();
        return x.Length > 3 && x.EndsWith('s') ? x[..^1] : x;
    }
}
```

- [ ] **Step 4: Correr el test para verlo pasar**

Run: `dotnet test backend --filter FiltroEtiquetasComidaTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Domain/Servicios/FiltroEtiquetasComida.cs tests/Calorias.Tests/FiltroEtiquetasComidaTests.cs
git -C backend commit -m "feat(vision): filtro de etiquetas no-comida con denylist y dedup"
```

---

### Task A2: Aplicar el filtro en el orquestador

**Files:**
- Modify: `backend/src/Calorias.Application/Servicios/OrquestadorAnalisisComida.cs:16-22`

- [ ] **Step 1: Insertar el filtro tras detectar etiquetas**

Reemplazar el bloque actual (pasos 1 y 2 del método `AnalizarAsync`):

```csharp
        // 1) Vision detecta etiquetas
        var etiquetasCrudas = await vision.DetectarAlimentosAsync(imagen, ct);
        var etiquetas = FiltroEtiquetasComida.Filtrar(etiquetasCrudas);
        if (etiquetas.Count == 0)
            throw new InvalidOperationException("No se detectaron alimentos en la imagen.");

        // 2) Tomamos los alimentos detectados (máx. 5) para consultar la nutrición
        var alimentos = etiquetas.Take(5).Select(e => e.Descripcion).ToList();
```

`FiltroEtiquetasComida` vive en `Calorias.Application.Servicios`, el mismo namespace que
`OrquestadorAnalisisComida`, así que **no hace falta añadir ningún `using`**.

- [ ] **Step 2: Verificar compilación + tests existentes**

Run: `dotnet test backend`
Expected: PASS (todos los tests previos + los de A1 siguen verdes).

- [ ] **Step 3: Commit**

```bash
git -C backend add src/Calorias.Application/Servicios/OrquestadorAnalisisComida.cs
git -C backend commit -m "feat(vision): orquestador aplica el filtro de etiquetas antes de USDA"
```

---

## Parte B — Selección de match USDA por relevancia (1B)

### Task B1: `SelectorMatchUsda` (puro, Application)

**Files:**
- Create: `backend/src/Calorias.Application/Servicios/SelectorMatchUsda.cs`
- Test: `backend/tests/Calorias.Tests/SelectorMatchUsdaTests.cs`

- [ ] **Step 1: Escribir el test que falla**

```csharp
using Calorias.Application.Servicios;
using Xunit;

namespace Calorias.Tests;

public class SelectorMatchUsdaTests
{
    private static CandidatoUsda C(int id, string desc, string tipo) => new(id, desc, tipo);

    [Fact]
    public void Prefiere_mayor_solape_de_tokens_con_la_query()
    {
        var cands = new[]
        {
            C(1, "Beverages, coffee substitute", "SR Legacy"),
            C(2, "Apple, raw", "Foundation"),
            C(3, "Apple pie, prepared from recipe", "SR Legacy"),
        };
        var mejor = SelectorMatchUsda.ElegirMejor(cands, "apple");
        Assert.Equal(2, mejor!.FdcId);
    }

    [Fact]
    public void Ante_igual_solape_prefiere_Foundation_sobre_SR_Legacy()
    {
        var cands = new[]
        {
            C(1, "Rice, white", "SR Legacy"),
            C(2, "Rice, white", "Foundation"),
        };
        var mejor = SelectorMatchUsda.ElegirMejor(cands, "white rice");
        Assert.Equal(2, mejor!.FdcId);
    }

    [Fact]
    public void Prefiere_descripcion_corta_cruda_sobre_preparacion_larga()
    {
        var cands = new[]
        {
            C(1, "Chicken, broilers or fryers, breast, meat only, raw", "SR Legacy"),
            C(2, "Chicken breast, oven-roasted, fat-free, sliced, with added solids", "SR Legacy"),
        };
        var mejor = SelectorMatchUsda.ElegirMejor(cands, "chicken breast");
        Assert.Equal(1, mejor!.FdcId);
    }

    [Fact]
    public void Lista_vacia_devuelve_null()
    {
        Assert.Null(SelectorMatchUsda.ElegirMejor(System.Array.Empty<CandidatoUsda>(), "x"));
    }
}
```

- [ ] **Step 2: Correr el test para verlo fallar**

Run: `dotnet test backend --filter SelectorMatchUsdaTests`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar el selector**

```csharp
namespace Calorias.Application.Servicios;

/// <summary>Candidato de búsqueda de USDA FoodData Central (datos mínimos para puntuar).</summary>
public record CandidatoUsda(int FdcId, string Description, string DataType);

/// <summary>
/// Elige el mejor match USDA para una consulta. Puro y testeable (sin red).
/// Puntúa por: solape de tokens query↔description (peso principal), bonus a Foundation,
/// preferencia por descripciones cortas/crudas y penalización por exceso de calificadores.
/// </summary>
public static class SelectorMatchUsda
{
    public static CandidatoUsda? ElegirMejor(IReadOnlyList<CandidatoUsda> candidatos, string query)
    {
        if (candidatos.Count == 0) return null;

        var qTokens = Tokenizar(query);
        CandidatoUsda? mejor = null;
        double mejorScore = double.NegativeInfinity;

        foreach (var c in candidatos)
        {
            var score = Puntuar(c, qTokens);
            if (score > mejorScore)
            {
                mejorScore = score;
                mejor = c;
            }
        }
        return mejor;
    }

    private static double Puntuar(CandidatoUsda c, HashSet<string> qTokens)
    {
        var dTokens = Tokenizar(c.Description);
        int solape = dTokens.Count(t => qTokens.Contains(t));

        double score = solape * 10.0;                                   // (a) solape de tokens
        if (c.DataType.Equals("Foundation", StringComparison.OrdinalIgnoreCase))
            score += 2.0;                                               // (b) bonus Foundation
        score -= dTokens.Count * 0.5;                                   // (c) preferir corto/crudo
        score -= c.Description.Count(ch => ch == ',') * 0.5;            // (d) penalizar calificadores
        if (dTokens.Contains("raw")) score += 1.0;                      // ligeramente a favor de "raw"
        return score;
    }

    private static HashSet<string> Tokenizar(string s) =>
        s.ToLowerInvariant()
         .Split(new[] { ' ', ',', '.', '(', ')', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
         .ToHashSet();
}
```

- [ ] **Step 4: Correr el test para verlo pasar**

Run: `dotnet test backend --filter SelectorMatchUsdaTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Application/Servicios/SelectorMatchUsda.cs tests/Calorias.Tests/SelectorMatchUsdaTests.cs
git -C backend commit -m "feat(usda): selector de match por relevancia (puro)"
```

---

## Parte C — Estimación de porción (1C)

### Task C1: `EstimadorPorcion` (puro, Application)

**Files:**
- Create: `backend/src/Calorias.Application/Servicios/EstimadorPorcion.cs`
- Test: `backend/tests/Calorias.Tests/EstimadorPorcionTests.cs`

- [ ] **Step 1: Escribir el test que falla**

```csharp
using Calorias.Application.Servicios;
using Xunit;

namespace Calorias.Tests;

public class EstimadorPorcionTests
{
    [Fact]
    public void Sin_porciones_usa_el_default()
    {
        Assert.Equal(150m, EstimadorPorcion.ElegirGramos(System.Array.Empty<decimal>()));
    }

    [Fact]
    public void Con_porciones_elige_la_primera_razonable()
    {
        // Ignora pesos no positivos y elige el primero válido.
        Assert.Equal(182m, EstimadorPorcion.ElegirGramos(new[] { 0m, 182m, 250m }));
    }

    [Fact]
    public void Descarta_pesos_absurdos_y_cae_al_default()
    {
        // > 1500 g se considera no representativo de una porción.
        Assert.Equal(150m, EstimadorPorcion.ElegirGramos(new[] { 5000m }));
    }
}
```

- [ ] **Step 2: Correr el test para verlo fallar**

Run: `dotnet test backend --filter EstimadorPorcionTests`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar el estimador**

```csharp
namespace Calorias.Application.Servicios;

/// <summary>
/// Elige los gramos de una porción a partir de los gramWeight de USDA (foodPortions).
/// Toma el primer peso "razonable" (0 &lt; g ≤ 1500); si no hay, usa un default sensato.
/// Puro y testeable.
/// </summary>
public static class EstimadorPorcion
{
    public const decimal GramosDefault = 150m;
    private const decimal MaxRazonable = 1500m;

    public static decimal ElegirGramos(IReadOnlyList<decimal> gramWeights)
    {
        foreach (var g in gramWeights)
            if (g > 0m && g <= MaxRazonable) return g;
        return GramosDefault;
    }
}
```

- [ ] **Step 4: Correr el test para verlo pasar**

Run: `dotnet test backend --filter EstimadorPorcionTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Application/Servicios/EstimadorPorcion.cs tests/Calorias.Tests/EstimadorPorcionTests.cs
git -C backend commit -m "feat(usda): estimador de porción a partir de foodPortions"
```

---

### Task C2: Integrar selector + porción + escalado en `ServicioNutricionUsda`

**Files:**
- Modify: `backend/src/Calorias.Infrastructure/Servicios/ServicioNutricionUsda.cs`

Esta tarea cambia el servicio HTTP (no se unit-testea con red; se verifica corriendo). El servicio ahora: busca (pageSize 15) → parsea candidatos → `SelectorMatchUsda.ElegirMejor` → lee macros del match (forma de `foods/search`) → pide `food/{fdcId}` para `foodPortions` → `EstimadorPorcion.ElegirGramos` → escala macros a esos gramos.

- [ ] **Step 1: Reemplazar el cuerpo de `ObtenerMacrosAsync`**

```csharp
using System.Text.Json;
using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Nutrición vía USDA FoodData Central. Por cada alimento detectado: busca candidatos,
/// elige el mejor por relevancia (SelectorMatchUsda), lee macros por 100 g, estima la
/// porción desde foodPortions (EstimadorPorcion) y escala los macros a esa porción.
/// </summary>
public class ServicioNutricionUsda(HttpClient http) : IServicioNutricion
{
    private const int EnergiaKcal = 1008;
    private const int EnergiaAtwaterGeneral = 2047;
    private const int EnergiaAtwaterEspecifico = 2048;
    private const int Proteina = 1003;
    private const int Carbohidratos = 1005;
    private const int Grasa = 1004;

    public async Task<ResultadoNutricional> ObtenerMacrosAsync(
        IReadOnlyList<string> alimentos, CancellationToken ct = default)
    {
        var resultados = new List<AlimentoNutricional>();
        var crudos = new List<string>();

        foreach (var alimento in alimentos)
        {
            var url = $"v1/foods/search?query={Uri.EscapeDataString(alimento)}"
                    + "&dataType=Foundation,SR%20Legacy&pageSize=15";

            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) continue;

            var json = await resp.Content.ReadAsStringAsync(ct);
            crudos.Add(json);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("foods", out var foods)
                || foods.GetArrayLength() == 0) continue;

            // Parsear candidatos y elegir el mejor por relevancia.
            var candidatos = new List<CandidatoUsda>();
            foreach (var f in foods.EnumerateArray())
            {
                int fdcId = f.TryGetProperty("fdcId", out var idEl) ? idEl.GetInt32() : 0;
                string desc = f.TryGetProperty("description", out var dEl) ? dEl.GetString() ?? "" : "";
                string tipo = f.TryGetProperty("dataType", out var tEl) ? tEl.GetString() ?? "" : "";
                if (fdcId != 0 && desc.Length > 0) candidatos.Add(new CandidatoUsda(fdcId, desc, tipo));
            }

            var mejor = SelectorMatchUsda.ElegirMejor(candidatos, alimento);
            if (mejor is null) continue;

            // Localizar el JsonElement del match elegido para leer sus macros (por 100 g).
            JsonElement food = default;
            foreach (var f in foods.EnumerateArray())
                if (f.TryGetProperty("fdcId", out var idEl) && idEl.GetInt32() == mejor.FdcId) { food = f; break; }

            decimal Nutriente(params int[] ids)
            {
                if (!food.TryGetProperty("foodNutrients", out var ns)) return 0m;
                foreach (var n in ns.EnumerateArray())
                {
                    if (n.TryGetProperty("nutrientId", out var nidEl)
                        && ids.Contains(nidEl.GetInt32())
                        && n.TryGetProperty("value", out var v)
                        && v.TryGetDecimal(out var dec))
                        return dec;
                }
                return 0m;
            }

            decimal cal100 = Nutriente(EnergiaKcal, EnergiaAtwaterGeneral, EnergiaAtwaterEspecifico);
            decimal pro100 = Nutriente(Proteina);
            decimal car100 = Nutriente(Carbohidratos);
            decimal gra100 = Nutriente(Grasa);

            // Estimar porción desde foodPortions del detalle del alimento (best-effort).
            decimal gramos = EstimadorPorcion.ElegirGramos(await ObtenerGramWeightsAsync(mejor.FdcId, ct));
            decimal factor = gramos / 100m;

            resultados.Add(new AlimentoNutricional(
                Nombre:        mejor.Description,
                Calorias:      Math.Round(cal100 * factor, 2),
                Proteinas:     Math.Round(pro100 * factor, 2),
                Carbohidratos: Math.Round(car100 * factor, 2),
                Grasas:        Math.Round(gra100 * factor, 2),
                Cantidad:      gramos,
                Unidad:        "g"));
        }

        var jsonCrudo = "[" + string.Join(",", crudos) + "]";
        return new ResultadoNutricional(resultados, jsonCrudo);
    }

    /// <summary>
    /// Pide el detalle del alimento y devuelve los gramWeight de sus foodPortions.
    /// Best-effort: ante cualquier fallo devuelve lista vacía (el estimador usa el default).
    /// </summary>
    private async Task<IReadOnlyList<decimal>> ObtenerGramWeightsAsync(int fdcId, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync($"v1/food/{fdcId}", ct);
            if (!resp.IsSuccessStatusCode) return System.Array.Empty<decimal>();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("foodPortions", out var ports)) return System.Array.Empty<decimal>();

            var pesos = new List<decimal>();
            foreach (var p in ports.EnumerateArray())
                if (p.TryGetProperty("gramWeight", out var g) && g.TryGetDecimal(out var dec))
                    pesos.Add(dec);
            return pesos;
        }
        catch
        {
            return System.Array.Empty<decimal>();
        }
    }
}
```

- [ ] **Step 2: Compilar (con la API parada)**

Run: `dotnet build backend`
Expected: PASS sin errores.

- [ ] **Step 3: Verificación manual del pipeline (real, con la API arriba)**

Arrancar la API (ver cabecera) y, autenticado desde el frontend web, analizar una foto sencilla (p.ej. una manzana). Esperado: el detalle muestra un nombre concreto de USDA (no genérico), `Cantidad` distinta de 100 cuando USDA tiene foodPortions, y macros escalados a esa porción.

> **Nota de verificación (riesgo del spec):** confirmar que `v1/food/{fdcId}` devuelve `foodPortions`. Si para Foundation/SR Legacy la propiedad tuviera otro nombre o forma, ajustar `ObtenerGramWeightsAsync` (sigue siendo best-effort: ante duda cae al default de 150 g, que ya mejora el 100 g a ciegas).

- [ ] **Step 4: Commit**

```bash
git -C backend add src/Calorias.Infrastructure/Servicios/ServicioNutricionUsda.cs
git -C backend commit -m "feat(usda): match por relevancia + porción estimada + escalado de macros"
```

---

## Parte D — Traducción de nombres EN→ES (1E)

### Task D1: Abstracción + diccionario + facade con caché (Application)

**Files:**
- Create: `backend/src/Calorias.Application/Abstractions/IServicioTraduccion.cs`
- Create: `backend/src/Calorias.Application/Servicios/DiccionarioAlimentos.cs`
- Create: `backend/src/Calorias.Application/Servicios/TraductorAlimentos.cs`
- Test: `backend/tests/Calorias.Tests/TraductorAlimentosTests.cs`

- [ ] **Step 1: Escribir el test que falla**

```csharp
using Calorias.Application.Abstractions;
using Calorias.Application.Servicios;
using Xunit;

namespace Calorias.Tests;

public class TraductorAlimentosTests
{
    private sealed class FakeTraduccion(string? respuesta) : IServicioTraduccion
    {
        public int Llamadas { get; private set; }
        public Task<string?> TraducirAlEspanolAsync(string textoIngles, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(respuesta);
        }
    }

    [Fact]
    public async Task Termino_en_diccionario_no_llama_al_servicio()
    {
        var fake = new FakeTraduccion("NO-DEBERIA-USARSE");
        var t = new TraductorAlimentos(fake);
        var r = await t.TraducirNombreAsync("Apple");
        Assert.Equal("Manzana", r);
        Assert.Equal(0, fake.Llamadas);
    }

    [Fact]
    public async Task Termino_ausente_usa_el_servicio_y_cachea()
    {
        var fake = new FakeTraduccion("Ensalada de quinoa");
        var t = new TraductorAlimentos(fake);
        var r1 = await t.TraducirNombreAsync("Quinoa salad, prepared");
        var r2 = await t.TraducirNombreAsync("Quinoa salad, prepared");
        Assert.Equal("Ensalada de quinoa", r1);
        Assert.Equal("Ensalada de quinoa", r2);
        Assert.Equal(1, fake.Llamadas); // la 2ª vez sale de la caché
    }

    [Fact]
    public async Task Si_el_servicio_no_traduce_cae_al_ingles()
    {
        var fake = new FakeTraduccion(null);
        var t = new TraductorAlimentos(fake);
        var r = await t.TraducirNombreAsync("Exotic unknown dish");
        Assert.Equal("Exotic unknown dish", r);
    }
}
```

- [ ] **Step 2: Correr el test para verlo fallar**

Run: `dotnet test backend --filter TraductorAlimentosTests`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar la abstracción**

`IServicioTraduccion.cs`:

```csharp
namespace Calorias.Application.Abstractions;

/// <summary>Traduce un texto del inglés al español. Devuelve null si no puede traducir.</summary>
public interface IServicioTraduccion
{
    Task<string?> TraducirAlEspanolAsync(string textoIngles, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar el diccionario curado**

`DiccionarioAlimentos.cs` (ampliable; cubre alimentos comunes de una sola palabra y algunos compuestos):

```csharp
namespace Calorias.Application.Servicios;

/// <summary>Diccionario curado EN→ES de alimentos comunes (clave case-insensitive).</summary>
public static class DiccionarioAlimentos
{
    public static readonly IReadOnlyDictionary<string, string> EsPorIngles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apple"] = "Manzana",
            ["banana"] = "Plátano",
            ["orange"] = "Naranja",
            ["rice"] = "Arroz",
            ["white rice"] = "Arroz blanco",
            ["chicken"] = "Pollo",
            ["chicken breast"] = "Pechuga de pollo",
            ["beef"] = "Carne de res",
            ["pork"] = "Cerdo",
            ["fish"] = "Pescado",
            ["egg"] = "Huevo",
            ["eggs"] = "Huevos",
            ["bread"] = "Pan",
            ["milk"] = "Leche",
            ["cheese"] = "Queso",
            ["potato"] = "Papa",
            ["tomato"] = "Tomate",
            ["lettuce"] = "Lechuga",
            ["carrot"] = "Zanahoria",
            ["onion"] = "Cebolla",
            ["beans"] = "Frijoles",
            ["pasta"] = "Pasta",
            ["pizza"] = "Pizza",
            ["salad"] = "Ensalada",
            ["soup"] = "Sopa",
            ["yogurt"] = "Yogur",
            ["butter"] = "Mantequilla",
            ["avocado"] = "Aguacate",
            ["corn"] = "Maíz",
            ["strawberry"] = "Fresa",
            ["grapes"] = "Uvas",
            ["salmon"] = "Salmón",
            ["tuna"] = "Atún",
            ["shrimp"] = "Camarón",
            ["broccoli"] = "Brócoli",
            ["spinach"] = "Espinaca",
            ["mushroom"] = "Champiñón",
            ["coffee"] = "Café",
            ["water"] = "Agua",
            ["juice"] = "Jugo",
        };
}
```

- [ ] **Step 5: Implementar el facade con caché**

`TraductorAlimentos.cs`:

```csharp
using System.Collections.Concurrent;
using Calorias.Application.Abstractions;

namespace Calorias.Application.Servicios;

/// <summary>
/// Traductor híbrido de nombres de alimentos: diccionario curado → caché → servicio
/// (Cloud Translation) → inglés como último recurso. La caché se siembra con el
/// diccionario y persiste entre peticiones (registrar como singleton).
/// </summary>
public class TraductorAlimentos(IServicioTraduccion servicio)
{
    private readonly ConcurrentDictionary<string, string> cache =
        new(DiccionarioAlimentos.EsPorIngles, StringComparer.OrdinalIgnoreCase);

    public async Task<string> TraducirNombreAsync(string textoIngles, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(textoIngles)) return textoIngles;
        if (cache.TryGetValue(textoIngles, out var hit)) return hit;

        var traducido = await servicio.TraducirAlEspanolAsync(textoIngles, ct);
        var final = string.IsNullOrWhiteSpace(traducido) ? textoIngles : traducido!;
        cache[textoIngles] = final;
        return final;
    }
}
```

> Nota: `new ConcurrentDictionary<string,string>(IEnumerable<KeyValuePair>, IEqualityComparer)` siembra la caché con el diccionario en el constructor.

- [ ] **Step 6: Correr el test para verlo pasar**

Run: `dotnet test backend --filter TraductorAlimentosTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git -C backend add src/Calorias.Application/Abstractions/IServicioTraduccion.cs src/Calorias.Application/Servicios/DiccionarioAlimentos.cs src/Calorias.Application/Servicios/TraductorAlimentos.cs tests/Calorias.Tests/TraductorAlimentosTests.cs
git -C backend commit -m "feat(i18n): traductor hibrido de alimentos (diccionario + cache + fallback)"
```

---

### Task D2: Implementación Google Cloud Translation + DI

**Files:**
- Modify: `backend/src/Calorias.Infrastructure/Calorias.Infrastructure.csproj`
- Create: `backend/src/Calorias.Infrastructure/Servicios/ServicioTraduccionGoogle.cs`
- Modify: `backend/src/Calorias.Infrastructure/DependencyInjection.cs`

**Prerrequisito de setup (manual, una vez):** habilitar **Cloud Translation API** en el proyecto GCP de la service account (`calorias-498417`). Si no se habilita, el servicio captura el error y cae al inglés (la app no se rompe), pero no traducirá lo que no esté en el diccionario.

- [ ] **Step 1: Añadir el paquete NuGet del cliente**

Run: `dotnet add backend/src/Calorias.Infrastructure package Google.Cloud.Translation.V2`
Expected: añade el `PackageReference` (versión estable más reciente) y restaura sin error.

- [ ] **Step 2: Implementar `ServicioTraduccionGoogle`**

```csharp
using Calorias.Application.Abstractions;
using Google.Cloud.Translation.V2;

namespace Calorias.Infrastructure.Servicios;

/// <summary>
/// Traducción EN→ES vía Google Cloud Translation v2. Usa la misma credencial ADC
/// (GOOGLE_APPLICATION_CREDENTIALS) que Vision. Best-effort: ante error devuelve null
/// para que el facade caiga al diccionario/inglés.
/// </summary>
public class ServicioTraduccionGoogle(TranslationClient cliente) : IServicioTraduccion
{
    public async Task<string?> TraducirAlEspanolAsync(string textoIngles, CancellationToken ct = default)
    {
        try
        {
            var r = await cliente.TranslateTextAsync(
                text: textoIngles, targetLanguage: "es", sourceLanguage: "en", cancellationToken: ct);
            return r?.TranslatedText;
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 3: Registrar en DI**

En `DependencyInjection.cs`, dentro de `AddInfrastructure`, tras el registro de Vision añadir:

```csharp
        // Google Cloud Translation: misma credencial ADC que Vision (singleton, thread-safe).
        services.AddSingleton(_ => TranslationClient.Create());
        services.AddSingleton<IServicioTraduccion, ServicioTraduccionGoogle>();
        services.AddSingleton<TraductorAlimentos>();
```

Añadir los `using` necesarios al inicio del archivo:

```csharp
using Calorias.Application.Servicios;
using Google.Cloud.Translation.V2;
```

- [ ] **Step 4: Compilar (API parada)**

Run: `dotnet build backend`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Infrastructure/
git -C backend commit -m "feat(i18n): ServicioTraduccionGoogle (Cloud Translation) + registro DI"
```

---

### Task D3: Traducir nombres en el orquestador (último paso)

**Files:**
- Modify: `backend/src/Calorias.Application/Servicios/OrquestadorAnalisisComida.cs`

La traducción va **después** de componer los detalles y calcular `ConfianzaDeteccion` (que se casa con etiquetas en inglés), para no romper el match.

- [ ] **Step 1: Inyectar el traductor**

Cambiar la firma del constructor primario:

```csharp
public class OrquestadorAnalisisComida(
    IServicioVision vision,
    IServicioNutricion nutricion,
    TraductorAlimentos traductor,
    ILogger<OrquestadorAnalisisComida> logger)
```

- [ ] **Step 2: Traducir los nombres tras armar los totales**

Justo antes de `return registro;`, añadir:

```csharp
        // Traducción EN→ES como último paso (el match/confianza ya se hicieron en inglés).
        foreach (var d in registro.Detalles)
            d.NombreAlimento = await traductor.TraducirNombreAsync(d.NombreAlimento, ct);
```

- [ ] **Step 3: Verificar tests existentes + compilación**

Run: `dotnet test backend`
Expected: PASS (todos verdes; `OrquestadorAnalisisComida` no tiene unit test propio, pero debe compilar y el resto sigue verde).

- [ ] **Step 4: Verificación manual (real)**

Analizar una foto desde el frontend web. Esperado: los nombres del detalle aparecen en español (los comunes vía diccionario; el resto vía Cloud si la API está habilitada; si no, quedan en inglés sin romper).

- [ ] **Step 5: Commit**

```bash
git -C backend add src/Calorias.Application/Servicios/OrquestadorAnalisisComida.cs
git -C backend commit -m "feat(i18n): orquestador traduce nombres de alimentos a espanol"
```

---

## Parte E — Corrección manual de porciones (1D)

### Task E1: Servicio de corrección (Application + Infrastructure)

**Files:**
- Create: `backend/src/Calorias.Application/Abstractions/IServicioCorreccionPorciones.cs`
- Create: `backend/src/Calorias.Infrastructure/Servicios/ServicioCorreccionPorciones.cs`
- Test: `backend/tests/Calorias.Tests/ServicioCorreccionPorcionesTests.cs`

- [ ] **Step 1: Escribir el test que falla**

```csharp
using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Calorias.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calorias.Tests;

public class ServicioCorreccionPorcionesTests
{
    private static CaloriasDbContext NuevaDb() =>
        new(new DbContextOptionsBuilder<CaloriasDbContext>()
            .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
            .Options);

    private static RegistroComida ComidaConUnDetalle(out Guid detalleId)
    {
        var det = new DetalleComida
        {
            NombreAlimento = "Manzana", Cantidad = 100m, UnidadMedida = "g",
            Calorias = 52m, Proteinas = 0.3m, Carbohidratos = 14m, Grasas = 0.2m,
        };
        detalleId = det.Id;
        var reg = new RegistroComida
        {
            UsuarioId = "u1", FechaLocal = new DateOnly(2026, 6, 9), Tipo = TipoComida.Snack,
            Detalles = new List<DetalleComida> { det },
            CaloriasTotales = 52m, ProteinasTotales = 0.3m, CarbohidratosTotales = 14m, GrasasTotales = 0.2m,
        };
        return reg;
    }

    [Fact]
    public async Task Reescala_macros_proporcional_y_recalcula_totales()
    {
        using var db = NuevaDb();
        var reg = ComidaConUnDetalle(out var detId);
        db.Registros.Add(reg);
        await db.SaveChangesAsync();

        var svc = new ServicioCorreccionPorciones(db, new ServicioResumenDiario(db));
        var actualizado = await svc.CorregirAsync("u1", reg.Id,
            new[] { new CorreccionDetalle(detId, 200m) });

        Assert.NotNull(actualizado);
        var d = actualizado!.Detalles.Single();
        Assert.Equal(200m, d.Cantidad);
        Assert.Equal(104m, d.Calorias);          // 52 * (200/100)
        Assert.Equal(104m, actualizado.CaloriasTotales);
        var rollup = await db.ResumenesDiarios.SingleAsync();
        Assert.Equal(104m, rollup.CaloriasTotal);
    }

    [Fact]
    public async Task Cantidad_cero_elimina_el_detalle()
    {
        using var db = NuevaDb();
        var reg = ComidaConUnDetalle(out var detId);
        db.Registros.Add(reg);
        await db.SaveChangesAsync();

        var svc = new ServicioCorreccionPorciones(db, new ServicioResumenDiario(db));
        var actualizado = await svc.CorregirAsync("u1", reg.Id,
            new[] { new CorreccionDetalle(detId, 0m) });

        Assert.NotNull(actualizado);
        Assert.Empty(actualizado!.Detalles);
        Assert.Equal(0m, actualizado.CaloriasTotales);
        // Sin detalles ni comidas, el rollup del día se borra.
        Assert.False(await db.ResumenesDiarios.AnyAsync());
    }

    [Fact]
    public async Task Registro_de_otro_usuario_devuelve_null()
    {
        using var db = NuevaDb();
        var reg = ComidaConUnDetalle(out var detId);
        db.Registros.Add(reg);
        await db.SaveChangesAsync();

        var svc = new ServicioCorreccionPorciones(db, new ServicioResumenDiario(db));
        var actualizado = await svc.CorregirAsync("otro", reg.Id,
            new[] { new CorreccionDetalle(detId, 200m) });

        Assert.Null(actualizado);
    }
}
```

- [ ] **Step 2: Correr el test para verlo fallar**

Run: `dotnet test backend --filter ServicioCorreccionPorcionesTests`
Expected: FAIL de compilación.

- [ ] **Step 3: Implementar la abstracción**

`IServicioCorreccionPorciones.cs`:

```csharp
using Calorias.Domain.Entities;

namespace Calorias.Application.Abstractions;

/// <summary>Una corrección de porción: nuevos gramos para un detalle (0 = eliminar).</summary>
public record CorreccionDetalle(Guid DetalleId, decimal CantidadG);

public interface IServicioCorreccionPorciones
{
    /// <summary>
    /// Reescala (proporcional) o elimina detalles de un registro propio del usuario,
    /// recalcula totales y el rollup diario. Devuelve el registro actualizado, o null
    /// si no existe o no pertenece al usuario.
    /// </summary>
    Task<RegistroComida?> CorregirAsync(
        string usuarioId, Guid registroId,
        IReadOnlyList<CorreccionDetalle> correcciones, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar el servicio**

`ServicioCorreccionPorciones.cs`:

```csharp
using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;
using Calorias.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Infrastructure.Servicios;

public class ServicioCorreccionPorciones(CaloriasDbContext db, IServicioResumenDiario resumen)
    : IServicioCorreccionPorciones
{
    public async Task<RegistroComida?> CorregirAsync(
        string usuarioId, Guid registroId,
        IReadOnlyList<CorreccionDetalle> correcciones, CancellationToken ct = default)
    {
        var reg = await db.Registros
            .Include(r => r.Detalles)
            .FirstOrDefaultAsync(r => r.Id == registroId && r.UsuarioId == usuarioId, ct);
        if (reg is null) return null;

        var porId = correcciones.ToDictionary(c => c.DetalleId, c => c.CantidadG);

        foreach (var d in reg.Detalles.ToList())
        {
            if (!porId.TryGetValue(d.Id, out var nuevaG)) continue;

            if (nuevaG <= 0m)
            {
                reg.Detalles.Remove(d);
                db.Remove(d);
                continue;
            }

            if (d.Cantidad > 0m)
            {
                var f = nuevaG / d.Cantidad;
                d.Calorias      = Math.Round(d.Calorias * f, 2);
                d.Proteinas     = Math.Round(d.Proteinas * f, 2);
                d.Carbohidratos = Math.Round(d.Carbohidratos * f, 2);
                d.Grasas        = Math.Round(d.Grasas * f, 2);
            }
            d.Cantidad = nuevaG;
        }

        reg.CaloriasTotales      = reg.Detalles.Sum(x => x.Calorias);
        reg.ProteinasTotales     = reg.Detalles.Sum(x => x.Proteinas);
        reg.CarbohidratosTotales = reg.Detalles.Sum(x => x.Carbohidratos);
        reg.GrasasTotales        = reg.Detalles.Sum(x => x.Grasas);

        await db.SaveChangesAsync(ct);
        await resumen.RecalcularDiaAsync(usuarioId, reg.FechaLocal, ct);
        return reg;
    }
}
```

- [ ] **Step 5: Correr el test para verlo pasar**

Run: `dotnet test backend --filter ServicioCorreccionPorcionesTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git -C backend add src/Calorias.Application/Abstractions/IServicioCorreccionPorciones.cs src/Calorias.Infrastructure/Servicios/ServicioCorreccionPorciones.cs tests/Calorias.Tests/ServicioCorreccionPorcionesTests.cs
git -C backend commit -m "feat(comidas): servicio de correccion de porciones (reescalado + rollup)"
```

---

### Task E2: Exponer `detalleId` en el DTO + endpoint PUT

**Files:**
- Modify: `backend/src/Calorias.Api/Dtos/AnalisisComidaDto.cs`
- Create: `backend/src/Calorias.Api/Dtos/CorreccionPorcionesDto.cs`
- Modify: `backend/src/Calorias.Api/Controllers/ComidasController.cs`
- Modify: `backend/src/Calorias.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Añadir `DetalleId` al DTO de detalle**

En `AnalisisComidaDto.cs`, cambiar el record `DetalleComidaDto` para que su primer campo sea el id:

```csharp
public record DetalleComidaDto(
    Guid DetalleId,
    string Nombre,
    decimal Calorias,
    decimal Proteinas,
    decimal Carbohidratos,
    decimal Grasas,
    decimal Cantidad,
    string Unidad);
```

- [ ] **Step 2: Crear el DTO de request de corrección**

`CorreccionPorcionesDto.cs`:

```csharp
namespace Calorias.Api.Dtos;

/// <summary>Cuerpo de PUT /api/comidas/{id}/porciones.</summary>
public record CorreccionPorcionesDto(IReadOnlyList<CorreccionDetalleDto> Detalles);

public record CorreccionDetalleDto(Guid DetalleId, decimal CantidadG);
```

- [ ] **Step 3: Refactor del mapeo a DTO + actualizar `Analizar` + nuevo endpoint**

En `ComidasController.cs`:

1. Añadir `IServicioCorreccionPorciones correccion` al constructor primario (junto a las otras deps):

```csharp
public class ComidasController(
    OrquestadorAnalisisComida orquestador,
    IServicioUsuarios usuarios,
    IServicioResumenDiario resumen,
    IServicioResumenPeriodos resumenPeriodos,
    IServicioCorreccionPorciones correccion,
    CaloriasDbContext db) : ControllerBase
```

2. Añadir un mapeo privado reutilizable y usarlo en `Analizar` (reemplazando el bloque que arma `dto` en líneas 56-69):

```csharp
        var dto = ADto(registro);
        return Ok(dto);
```

3. Al final de la clase (antes del `ComponerNombre`), añadir el helper de mapeo:

```csharp
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
```

4. Añadir el endpoint PUT tras `Resumen(...)`:

```csharp
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
```

Asegurar el `using Calorias.Application.Abstractions;` (ya presente) cubre `CorreccionDetalle`/`IServicioCorreccionPorciones`.

- [ ] **Step 4: Registrar el servicio en DI**

En `DependencyInjection.cs`, junto a los otros `AddScoped`:

```csharp
        services.AddScoped<IServicioCorreccionPorciones, ServicioCorreccionPorciones>();
```

- [ ] **Step 5: Compilar + tests (API parada)**

Run: `dotnet test backend`
Expected: PASS (todos verdes).

- [ ] **Step 6: Verificación manual (real)**

Con la API arriba: analizar una foto (POST). En la respuesta, cada detalle ahora trae `detalleId`. Llamar `PUT /api/comidas/{id}/porciones` con `{ "detalles": [{ "detalleId": "...", "cantidadG": 200 }] }` y un Bearer válido → 200 con totales reescalados. Probar `cantidadG: 0` → ese detalle desaparece.

- [ ] **Step 7: Commit**

```bash
git -C backend add src/Calorias.Api/ src/Calorias.Infrastructure/DependencyInjection.cs
git -C backend commit -m "feat(comidas): endpoint PUT porciones + detalleId en DTO"
```

---

### Task E3: Frontend — cliente API de corrección

**Files:**
- Modify: `frontend/src/api/comidas.ts`

- [ ] **Step 1: Extender el tipo `DetalleComida` y `RegistroComida`**

Reemplazar la interfaz `DetalleComida` (líneas 4-11) por:

```typescript
/** Un alimento detectado dentro de una comida (forma del DTO del backend). */
export interface DetalleComida {
  detalleId?: string;
  nombre?: string;
  calorias?: number;
  proteinas?: number;
  carbohidratos?: number;
  grasas?: number;
  cantidad?: number;
  unidad?: string;
}
```

`RegistroComida` ya incluye `id?: string` y `detalles?: DetalleComida[]`; no cambia.

- [ ] **Step 2: Añadir `actualizarPorciones`**

Al final del archivo (antes de `inferirMime` o tras él), añadir:

```typescript
export interface CorreccionPorcion {
  detalleId: string;
  cantidadG: number;
}

/** Corrige las porciones de una comida ya guardada y devuelve el registro actualizado. */
export async function actualizarPorciones(
  registroId: string,
  idToken: string,
  correcciones: CorreccionPorcion[],
): Promise<RegistroComida> {
  const url = `${API_BASE_URL}/api/comidas/${registroId}/porciones`;
  let res: Response;
  try {
    res = await fetch(url, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${idToken}`,
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ detalles: correcciones }),
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
  return (await res.json()) as RegistroComida;
}
```

Añadir el import de `API_BASE_URL` al inicio del archivo (junto al de `ANALIZAR_URL`):

```typescript
import { ANALIZAR_URL, API_BASE_URL } from '../config';
```

> Verificar en `frontend/src/config.ts` que `API_BASE_URL` está exportado (lo usa `resumen.ts`). Si el nombre difiere, alinear el import.

- [ ] **Step 3: Verificar tipos**

Run: `cd frontend ; .\node_modules\.bin\tsc --noEmit`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git -C frontend add src/api/comidas.ts
git -C frontend commit -m "feat(captura): cliente actualizarPorciones + tipos de detalle ampliados"
```

---

### Task E4: Frontend — tarjeta de resultado editable

**Files:**
- Modify: `frontend/src/screens/CaptureScreen.tsx`

Reemplaza la `ResultadoCard` de solo lectura por una editable: por alimento, gramos con steppers ± y ✕ para quitar, macros recalculados en vivo, y botón "Guardar correcciones" que llama a `actualizarPorciones`.

- [ ] **Step 1: Ampliar imports**

En el import de `../api/comidas`, añadir `actualizarPorciones` y el tipo `CorreccionPorcion`:

```typescript
import {
  analizarFoto,
  actualizarPorciones,
  ApiError,
  type CorreccionPorcion,
  type DetalleComida,
  type RegistroComida,
  type TipoComida,
} from '../api/comidas';
```

Añadir `TextInput` a los imports de `react-native` (se usa para editar gramos): en la lista existente de `react-native` añadir `TextInput`.

- [ ] **Step 2: Pasar `auth` y un callback de refresco a `ResultadoCard`**

En el render, cambiar `{resultado && <ResultadoCard registro={resultado} />}` por:

```tsx
        {resultado && (
          <ResultadoCard
            registro={resultado}
            idToken={auth.idToken}
            onActualizado={setResultado}
          />
        )}
```

- [ ] **Step 3: Reescribir `ResultadoCard` como editable**

Reemplazar toda la función `ResultadoCard` (líneas 170-197) por:

```tsx
function ResultadoCard({
  registro,
  idToken,
  onActualizado,
}: {
  registro: RegistroComida;
  idToken: string | null;
  onActualizado: (r: RegistroComida) => void;
}) {
  const { colors } = useTheme();
  const styles = makeStyles(colors);

  // Estado editable: gramos por detalle (clave = detalleId).
  const detallesBase = registro.detalles ?? [];
  const [gramos, setGramos] = useState<Record<string, number>>(() =>
    Object.fromEntries(
      detallesBase
        .filter((d) => d.detalleId)
        .map((d) => [d.detalleId as string, Math.round(d.cantidad ?? 100)]),
    ),
  );
  const [guardando, setGuardando] = useState(false);
  const [errGuardar, setErrGuardar] = useState<string | null>(null);

  // Factor de escala por detalle para previsualizar macros en vivo.
  const factor = (d: DetalleComida) => {
    const orig = d.cantidad ?? 100;
    const actual = gramos[d.detalleId as string] ?? orig;
    return orig > 0 ? actual / orig : 1;
  };

  const visibles = detallesBase.filter((d) => (gramos[d.detalleId as string] ?? 1) > 0);
  const totalKcal = visibles.reduce((s, d) => s + (d.calorias ?? 0) * factor(d), 0);
  const totalProt = visibles.reduce((s, d) => s + (d.proteinas ?? 0) * factor(d), 0);
  const totalCarb = visibles.reduce((s, d) => s + (d.carbohidratos ?? 0) * factor(d), 0);
  const totalGra = visibles.reduce((s, d) => s + (d.grasas ?? 0) * factor(d), 0);

  const setG = (id: string, v: number) => setGramos((g) => ({ ...g, [id]: Math.max(0, v) }));

  const guardar = async () => {
    if (!idToken || !registro.id) return;
    setGuardando(true);
    setErrGuardar(null);
    try {
      const correcciones: CorreccionPorcion[] = detallesBase
        .filter((d) => d.detalleId)
        .map((d) => ({ detalleId: d.detalleId as string, cantidadG: gramos[d.detalleId as string] ?? 0 }));
      const actualizado = await actualizarPorciones(registro.id, idToken, correcciones);
      onActualizado(actualizado);
    } catch (e) {
      setErrGuardar(e instanceof ApiError ? e.message : 'No se pudieron guardar las correcciones.');
    } finally {
      setGuardando(false);
    }
  };

  return (
    <View style={styles.card}>
      <Text style={styles.cardTitle}>Resultado</Text>
      <View style={styles.macroRow}>
        <Macro label="kcal" value={totalKcal} />
        <Macro label="Prot" value={totalProt} suffix="g" />
        <Macro label="Carbs" value={totalCarb} suffix="g" />
        <Macro label="Grasas" value={totalGra} suffix="g" />
      </View>

      <View style={styles.detalleList}>
        {detallesBase.map((d, i) => {
          const id = d.detalleId as string;
          const g = gramos[id] ?? Math.round(d.cantidad ?? 100);
          const eliminado = g <= 0;
          return (
            <View key={id ?? i} style={styles.editRow}>
              <View style={styles.editNombreCol}>
                <Text style={[styles.detalleNombre, eliminado && styles.eliminado]}>
                  {d.nombre ?? 'Alimento'}
                </Text>
                <Text style={styles.editKcal}>
                  {eliminado ? 'Eliminado' : `${Math.round((d.calorias ?? 0) * factor(d))} kcal`}
                </Text>
              </View>
              <View style={styles.editControls}>
                <Pressable onPress={() => setG(id, g - 10)} style={styles.stepBtn}>
                  <Text style={styles.stepBtnText}>−</Text>
                </Pressable>
                <TextInput
                  value={String(g)}
                  onChangeText={(t) => setG(id, parseInt(t.replace(/[^0-9]/g, '') || '0', 10))}
                  keyboardType="numeric"
                  style={styles.gramInput}
                />
                <Text style={styles.gramUnidad}>g</Text>
                <Pressable onPress={() => setG(id, g + 10)} style={styles.stepBtn}>
                  <Text style={styles.stepBtnText}>+</Text>
                </Pressable>
                <Pressable onPress={() => setG(id, 0)} style={styles.removeBtn}>
                  <Text style={styles.removeBtnText}>✕</Text>
                </Pressable>
              </View>
            </View>
          );
        })}
      </View>

      {errGuardar && <Text style={styles.errorText}>{errGuardar}</Text>}

      <Pressable
        onPress={guardar}
        disabled={guardando || !idToken}
        style={({ pressed }) => [styles.guardarBtn, (guardando || !idToken) && styles.analyzeBtnDisabled, pressed && styles.pressed]}
      >
        {guardando ? (
          <ActivityIndicator color={colors.bg} />
        ) : (
          <Text style={styles.guardarBtnText}>Guardar correcciones</Text>
        )}
      </Pressable>
    </View>
  );
}
```

- [ ] **Step 4: Añadir estilos nuevos**

En `makeStyles`, dentro del `StyleSheet.create`, añadir:

```typescript
  editRow: {
    flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between',
    borderTopWidth: 1, borderTopColor: colors.border, paddingTop: spacing.sm, gap: spacing.sm,
  },
  editNombreCol: { flexShrink: 1, gap: 2 },
  editKcal: { color: colors.textMuted, fontSize: 12 },
  eliminado: { textDecorationLine: 'line-through', color: colors.textMuted },
  editControls: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  stepBtn: {
    width: 30, height: 30, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center',
    backgroundColor: colors.surfaceAlt, borderWidth: 1, borderColor: colors.border,
  },
  stepBtnText: { color: colors.text, fontSize: 18, fontWeight: '800', lineHeight: 20 },
  gramInput: {
    minWidth: 44, textAlign: 'center', color: colors.text, fontSize: 14, fontWeight: '700',
    paddingVertical: 2, paddingHorizontal: 4, borderRadius: radius.sm,
    backgroundColor: colors.surfaceAlt, borderWidth: 1, borderColor: colors.border,
  },
  gramUnidad: { color: colors.textMuted, fontSize: 12 },
  removeBtn: { width: 30, height: 30, alignItems: 'center', justifyContent: 'center' },
  removeBtnText: { color: colors.danger, fontSize: 14, fontWeight: '800' },
  guardarBtn: {
    backgroundColor: colors.primary, borderRadius: radius.md, paddingVertical: spacing.sm,
    alignItems: 'center', justifyContent: 'center', minHeight: 44, marginTop: spacing.xs,
  },
  guardarBtnText: { color: colors.bg, fontWeight: '800', fontSize: 15 },
```

> Si `radius.sm` no existe en `theme.ts`, usar `radius.md`. Verificar las claves de `radius`/`spacing` en `frontend/src/theme.ts` antes de compilar.

- [ ] **Step 5: Verificar tipos**

Run: `cd frontend ; .\node_modules\.bin\tsc --noEmit`
Expected: PASS. Si `factor(d)` se queja por el tipo del parámetro, tipar `d: DetalleComida` e importar el tipo `DetalleComida` desde `../api/comidas`.

- [ ] **Step 6: Verificación manual (web)**

Analizar una foto → la tarjeta de resultado muestra cada alimento con gramos editables (± y campo numérico), kcal por ítem que cambia en vivo, ✕ que lo tacha, y totales que se recalculan. "Guardar correcciones" persiste y refresca con la respuesta del backend (los totales deben coincidir con lo previsualizado).

- [ ] **Step 7: Commit**

```bash
git -C frontend add src/screens/CaptureScreen.tsx
git -C frontend commit -m "feat(captura): correccion manual de porciones (editar gramos / eliminar)"
```

---

## Self-review / cierre

- [ ] `dotnet test backend` → todos verdes (incluye los nuevos: Filtro, Selector, Estimador, Traductor, Correccion).
- [ ] `frontend\.\node_modules\.bin\tsc --noEmit` limpio.
- [ ] Verificación E2E manual en web del flujo completo: analizar → nombres en español, porción ≠ 100 g, corregir gramos/eliminar, totales y dashboard coherentes.
- [ ] Confirmar que la **Cloud Translation API** está habilitada en GCP (si no, los nombres no-diccionario quedan en inglés — degradación aceptada, no error).
- [ ] Abrir PR por la web en **ambos** repos (`backend` → main, `frontend` → master) con `superpowers:finishing-a-development-branch`.

## Orden de ejecución recomendado

A1 → A2 → B1 → C1 → C2 → D1 → D2 → D3 → E1 → E2 → E3 → E4. Las partes A–D son backend puro/integración; E cruza backend y frontend. El frontend (E3, E4) puede ir en paralelo una vez E2 expone `detalleId` y el endpoint.
