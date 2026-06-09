# Diseño: calidad de datos nutricionales (filtrado, match, porciones, corrección, traducción)

- **Fecha:** 2026-06-09
- **Estado:** Aprobado (pendiente de plan de implementación)
- **Repos afectados:** `backend` (.NET 10, Clean Architecture) y `frontend` (Expo/Mythik, target web — pantalla de captura)

## 1. Objetivo

Mejorar la calidad de los datos nutricionales que produce el pipeline foto → Vision → USDA,
que hoy tiene tres limitaciones conocidas y muy visibles, más una de presentación:

1. **Etiquetas genéricas** de Vision ("Food", "Dish", "Tableware"…) se cuentan como alimentos.
2. **Selección de match en USDA** = primer resultado, sin criterio de relevancia.
3. **Porciones a ciegas**: todo se asume a 100 g.
4. **Nombres en inglés**: el usuario ve nombres de Vision/USDA en inglés.

## 2. Decisiones tomadas (brainstorming)

| Tema | Decisión |
|---|---|
| Filtrado de etiquetas | **Denylist** curada de términos genéricos/no-comida + de-duplicación |
| Match USDA | **Scoring por relevancia** (no primer match) |
| Porciones | **Porción USDA cuando exista** + fallback sensato por categoría + **corrección manual** |
| Corrección manual | **Editar-después-de-guardar** (el flujo actual ya persiste al analizar) |
| Unidad de porción | **Gramos** (neutro; evita modificadores en inglés "1 cup/medium") |
| Eliminar ítems | Sí — `cantidadG == 0` elimina el detalle (corrige sobre-detección) |
| Traducción nombres | **Híbrido**: diccionario curado EN→ES + Google Cloud Translation como fallback |
| Idioma de proceso | Pipeline sigue en **inglés**; traducción es el último paso antes de persistir |

## 3. Estado actual (verificado contra código)

- `ServicioVisionGoogle.DetectarAlimentosAsync` → `DetectLabels(maxResults: 15)`, filtra `Score ≥ 0.6`,
  devuelve `EtiquetaDetectada(Descripcion, Confianza)`.
- `OrquestadorAnalisisComida.AnalizarAsync` → toma `etiquetas.Take(5)`, las manda a USDA, cada una
  se convierte en un `DetalleComida`. La confianza se asigna casando `NombreAlimento.Contains(etiqueta)`.
- `ServicioNutricionUsda.ObtenerMacrosAsync` → por alimento, `foods/search?query=…&dataType=Foundation,SR Legacy&pageSize=1`,
  toma `foods[0]`, lee macros por **100 g**, fija `Cantidad=100, Unidad="g"`.
- El flujo `POST /api/comidas/analizar` **guarda inmediatamente** y devuelve `AnalisisComidaDto`
  (sin `detalleId` por ítem).
- Frontend `CaptureScreen` muestra el resultado (solo lectura). `api/comidas.ts` tipa `DetalleComida`
  sin id ni cantidad.

## 4. Diseño

### 4.1 Filtro de etiquetas (1A) — backend, puro

- Nueva clase estática `FiltroEtiquetasComida` en `Calorias.Domain/Servicios`.
- Entrada `IReadOnlyList<EtiquetaDetectada>` → salida filtrada y de-duplicada.
- **Denylist** (case-insensitive, comparación exacta del label) de términos genéricos/no-comida:
  *Food, Dish, Cuisine, Ingredient, Recipe, Produce, Meal, Tableware, Dishware, Plate, Bowl,
  Cutlery, Fork, Spoon, Knife, Drink, Drinkware, Breakfast, Lunch, Dinner, Brunch, Supper,
  Fast food, Junk food, Comfort food, Finger food, Whole food, Natural foods, Vegetarian food,
  Superfood, Staple food, Side dish, Garnish, Snack, Tablecloth* (lista ampliable).
- **De-duplicación**: normaliza (lower, trim, singular/plural simple) y descarta duplicados; ante
  solapamiento conserva la de mayor confianza.
- Si tras filtrar queda vacío → el orquestador mantiene el error actual "No se detectaron alimentos".
- `OrquestadorAnalisisComida` aplica el filtro antes de consultar USDA.
- **Tests**: etiquetas mixtas (genéricas + reales), todo-genérico → vacío, duplicados singular/plural.

### 4.2 Selección de match USDA por relevancia (1B) — backend, scoring puro

- `ServicioNutricionUsda`: subir `pageSize` a ~15, mantener `dataType=Foundation,SR Legacy`.
- Nueva función pura `SelectorMatchUsda` (Domain o Application, sin red): recibe los candidatos
  parseados (description, dataType, fdcId, foodPortions) + la query, devuelve el mejor por score:
  - (a) **solape de tokens** query ↔ description (peso principal),
  - (b) preferir `Foundation` sobre `SR Legacy`,
  - (c) preferir descripciones **crudas/cortas** ("Apple, raw") sobre preparaciones largas,
  - (d) penalizar exceso de calificadores/comas.
- El parseo HTTP queda en el servicio; el scoring se extrae a la función pura para testear sin red.
- **Tests**: dado un set de candidatos de ejemplo y una query, elige el esperado; desempates.

### 4.3 Estimación de porción (1C) — backend

- Tras elegir el alimento, leer su porción casera de USDA: `foodPortions[].gramWeight`. Elegir una
  representativa (p.ej. la primera razonable; criterio explícito en el plan).
- **Fallback** cuando no haya `foodPortions`: porción típica (constante sensata, p.ej. 150 g) — **no** 100 g a ciegas.
- Escalar macros linealmente de por-100 g a los gramos elegidos. `DetalleComida.Cantidad`/`UnidadMedida`
  reflejan la porción real (ej. `150 / "g"`).
- **Riesgo a verificar en el plan:** `foods/search` puede **no** traer `foodPortions`. El plan debe
  verificar la forma real de la API USDA y, si hace falta, hacer un GET de detalle `food/{fdcId}`
  (≤5 llamadas por análisis) para obtener las porciones.

### 4.4 Corrección manual (1D) — backend + frontend (editar-después-de-guardar)

**Backend**
- Nuevo endpoint `PUT /api/comidas/{id}/porciones` (`[Authorize]`, verifica que el registro sea del usuario).
- Cuerpo: `[{ detalleId: Guid, cantidadG: decimal }]`.
- Por cada detalle: **reescala macros proporcionalmente** desde los valores guardados
  (`nuevo = viejo * cantidadG / cantidadActual`); si `cantidadG == 0` → **elimina** ese detalle.
- Recalcula totales del `RegistroComida` **y** el rollup diario (`RecalcularDiaAsync`) en una transacción.
- Devuelve el `AnalisisComidaDto` actualizado.
- Se añade `detalleId` al `DetalleComidaDto` (hoy no viaja) para poder editar por ítem.
- **Tests**: reescalado proporcional, eliminación por 0, recálculo de totales + rollup, ownership (403 ajeno).

**Frontend (`CaptureScreen`)**
- Tras analizar, la tarjeta de resultado se vuelve **editable**: por alimento → nombre + campo de gramos
  con steppers ±, macros recalculados en vivo (cliente), botón ✕ para quitar.
- Botón "Guardar correcciones" → `PUT /api/comidas/{id}/porciones`; refresca con la respuesta.
- Extender el tipo cliente `DetalleComida` (id, cantidad, unidad) y añadir `actualizarPorciones()` en `api/comidas.ts`.

### 4.5 Traducción de nombres EN→ES (1E) — backend, híbrido

- **Diccionario curado** EN→ES de alimentos comunes (en código; estructura de datos en Infrastructure/Domain).
- Abstracción `IServicioTraduccion` + impl `ServicioTraduccionGoogle` (Cloud Translation API, misma
  service account `GOOGLE_APPLICATION_CREDENTIALS`), usada **solo como fallback**.
- Orquestación `TraductorAlimentos`: diccionario → cloud fallback → **inglés como último recurso**.
- **Caché** en memoria EN→ES (p.ej. `ConcurrentDictionary`) sembrada con el diccionario, para no repetir
  llamadas a Cloud por término ya visto.
- Se aplica como **último paso** al componer `DetalleComida` (después del match y del cálculo de confianza,
  que siguen en inglés). Se **persiste el nombre en español** en `NombreAlimento`.
- El inglés crudo se conserva en los payloads jsonb (`PayloadVisionJson`/`PayloadNutritionixJson`) para auditoría.
- **Prerrequisitos de setup**: habilitar **Cloud Translation API** en el proyecto GCP; añadir el paquete
  NuGet del cliente (`Google.Cloud.Translation.V2` o `Google.Cloud.Translate.V3` — el plan fija cuál).
- **Tests**: término en diccionario → ES sin llamar a cloud; término ausente → usa fallback (mock); caché evita 2ª llamada.

## 5. Sin migración de BD

No hay columnas nuevas. El reescalado de porciones es proporcional sobre lo ya guardado, y el nombre en
español se persiste en la columna `NombreAlimento` existente. (Procesamiento interno —match/confianza— ocurre
antes de traducir, así que no se rompe.)

## 6. Orden sugerido de implementación

1. 1A Filtro de etiquetas (puro, sin deps).
2. 1B Scoring USDA (puro) + integración en `ServicioNutricionUsda`.
3. 1C Porciones (verificar forma de API USDA primero).
4. 1E Traducción (diccionario + servicio + caché).
5. 1D Corrección manual (endpoint + DTO + UI).

## 7. Fuera de alcance

- Estimación de porción por visión real (depth/objeto de referencia) — limitación de input, se aborda luego.
- OAuth nativo / dev build móvil (sesión posterior).
