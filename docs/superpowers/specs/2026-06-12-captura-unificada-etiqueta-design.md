# Captura unificada + edición de etiqueta — Diseño

Fecha: 2026-06-12
Sub-proyecto A (objetivos de sesión 1 + 4). Sub-proyecto B (dev build + OAuth nativo) es spec aparte.

## Objetivo

Fusionar los dos flujos de captura (analizar plato / escanear etiqueta), hoy un toggle con
dos caminos duplicados, en una sola pantalla coherente; permitir editar **todos** los datos
de una etiqueta antes de guardar (nombre, tamaño de porción, base de medida y macros); y
soportar etiquetas expresadas **por 100 g/mL** además de **por porción**, con mejor estado de
error y guía de encuadre.

No-objetivos: auto-detección plato/etiqueta, edición de comidas del historial, OAuth/dev build.

## Modelo de interacción (decisión: opción B)

Una sola pantalla `CaptureScreen` con un **segmented control discreto** arriba: `Plato` /
`Etiqueta`. Todo lo demás se comparte entre modos: selector de tipo de comida, preview de
foto, botones Tomar foto / Galería, zona de resultado editable y manejo de errores. El botón
de acción es uno solo y cambia su etiqueta según el modo (`Analizar foto` / `Leer etiqueta`).
Se elimina la duplicación actual de bloques de botón y de resultado.

Descartado: auto-detección total (A) y auto-detección con confirmación (C) — añaden una
clasificación extra (latencia/costo) y riesgo de confundir plato/etiqueta, caro en una app de
calorías. Pueden añadirse después sin reescribir B.

## Contrato: base de medida (porción | 100 g)

La etiqueta gana un discriminador de base. Forma canónica (frontend `EtiquetaNutricional`):

```
EtiquetaNutricional {
  nombreProducto?: string
  tamPorcion: number            // tamaño de UNA porción
  unidadPorcion: string         // 'g' | 'mL' | 'unidad' | ...
  porcionesPorEnvase?: number | null
  base: 'porcion' | 'cien'      // NUEVO: en qué base vienen los macros
  caloriasPorBase: number       // (renombrado desde caloriasPorPorcion)
  proteinaPorBase: number
  carbosPorBase: number
  grasasPorBase: number
}
```

### Reglas de cálculo (las aplica el cliente y el backend al guardar)
- `porPorcion(valor) = base === 'cien' ? valor * tamPorcion / 100 : valor`
- `totalConsumido(valor) = porPorcion(valor) * nº porciones`
- `base = 'cien'` **exige** `unidadPorcion` en masa/volumen (`g` o `mL`). Si no, se trata como
  `'porcion'` (la conversión por 100 no tiene sentido para 'unidad').

## Backend (.NET) — puntos de cambio

1. **`Calorias.Application/Abstractions/IServicioEtiquetaNutricional.cs`** — record
   `EtiquetaNutricional`: añadir `BaseMedida Base` (enum nuevo `{ Porcion, Cien }`) y renombrar
   los 4 macros a `CaloriasPorBase`/`ProteinaPorBase`/`CarbosPorBase`/`GrasasPorBase`. Añadir un
   método/helper puro que devuelva los valores **por porción** aplicando la regla de conversión
   (con la salvaguarda de unidad no-masa → trata como `Porcion`).

2. **`Calorias.Infrastructure/Servicios/ServicioEtiquetaGemini.cs`** — ampliar el `Prompt` y el
   `responseSchema` para incluir `base` (`STRING`, valores `"porcion"|"cien"`) e instruir a
   Gemini: leer la base real de la etiqueta, manejar etiquetas en cualquier idioma, derivar
   `tamPorcion` en g/mL cuando la base sea `cien`. Mapear el `base` leído al enum; default
   `Porcion` si falta o es inválido. Mantener centinela `calorias<=0 → null`. Mantener modelo
   `gemini-2.5-flash-lite` y API key `Gemini:ApiKey`.

3. **`Calorias.Application/Servicios/RegistroDesdeEtiqueta.cs`** — usar el helper de conversión
   a por-porción antes de escalar por `porciones`. El resto (1 detalle, totales) igual.

4. **`Calorias.Api/Dtos/EtiquetaDtos.cs`** — `EtiquetaNutricionalDto` y `GuardarEtiquetaDto`:
   añadir `string Base` y renombrar macros a `...PorBase`.

5. **`Calorias.Api/Controllers/ComidasController.cs`** — `LeerEtiqueta` propaga `Base` al DTO;
   `GuardarEtiqueta` parsea `Base` (default `Porcion` si inválido) al reconstruir la
   `EtiquetaNutricional`. Validaciones existentes intactas. Mensaje de error de `leer-etiqueta`
   sin cambios de fondo (el detalle de encuadre lo da el frontend).

## Frontend (Expo/React Native) — puntos de cambio

1. **`src/api/comidas.ts`** — `EtiquetaNutricional` y `GuardarEtiquetaPayload`: añadir
   `base: 'porcion' | 'cien'` y renombrar macros a `...PorBase`. Sin otros cambios de cliente.

2. **`src/screens/etiquetaCalculos.ts` (NUEVO)** — helpers puros (espejo de `dashboardFechas.ts`):
   `porPorcion(valor, base, tamPorcion, unidad)`, `totalEtiqueta(etiqueta, porciones)`,
   `alternarBase(...)` para el toggle (recalcula los valores mostrados al cambiar de base sin
   perder la cantidad real). Sin dependencias de RN → testeable como función pura.

3. **`src/screens/CaptureScreen.tsx`** — unificar:
   - Segmented control `Plato`/`Etiqueta` que comparte preview, tipo, botones de foto y la
     zona de resultado. Un solo botón de acción.
   - Tarjeta de etiqueta editable: inputs para nombre, tamaño+unidad de porción, **toggle de
     base** (por porción / por 100 g), los 4 macros (en la base activa) y nº de porciones.
     **Total recalculado en vivo** con `etiquetaCalculos`.
   - Estado de error/vacío mejorado: tarjeta con título claro ("No pude leer la tabla
     nutricional") + bullets de encuadre (tabla completa, enfocada, derecha), reutilizable para
     "no es etiqueta / faltan datos / error de red" con copy adaptado. Hint de encuadre bajo el
     preview en modo Etiqueta.

## Errores y casos límite
- Gemini no lee etiqueta → backend 400 (ya existe) → frontend muestra la tarjeta de guía.
- `base='cien'` con unidad no-masa → backend y frontend lo tratan como `porcion` (sin romper).
- Usuario edita un macro a vacío/negativo → se trata como 0 (igual que el flujo de plato).
- Cambiar de base en la edición convierte los valores mostrados, no la cantidad real.

## Testing
- **Backend (44 verdes hoy, TDD):** ampliar `RegistroDesdeEtiquetaTests`: caso `base=Cien`
  (conversión + escalado correctos), caso unidad no-masa con `Cien` (fallback a `Porcion`),
  caso `base=Porcion` (sin regresión). Test del helper de conversión puro.
- **Frontend:** `tsc --noEmit` limpio (`frontend/.\node_modules\.bin\tsc`). El helper
  `etiquetaCalculos.ts` queda como función pura aislada. Verificación E2E en web por el usuario
  (modo plato sin regresión, modo etiqueta por-porción y por-100g, edición, total en vivo,
  estado de error).

## Entrega
Subagent-driven development + TDD. Rama de feature en cada repo, PR por la web, merge por el
usuario, sync local + borrar rama. Specs/planes en `backend/docs/superpowers/`.
