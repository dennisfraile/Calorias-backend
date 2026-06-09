# Diseño: OCR de etiqueta nutricional (escaneo de productos envasados)

- **Fecha:** 2026-06-09
- **Estado:** Aprobado (pendiente de plan de implementación)
- **Repos afectados:** `backend` (.NET 10) y `frontend` (Expo/Mythik, target web — pantalla de captura)

## 1. Objetivo

Permitir registrar un producto envasado **escaneando su etiqueta nutricional** (en vez del
reconocimiento general de Vision, que no lee la tabla nutricional ni reconoce productos
específicos). Es la mejora de exactitud más grande pendiente: hoy una bebida/producto se
estima mal porque Vision `DetectLabels` etiqueta por parecido visual, no lee "Datos
Nutricionales".

## 2. Decisiones tomadas (brainstorming)

| Tema | Decisión |
|---|---|
| Motor de extracción | **Gemini multimodal (free tier, Google AI Studio)**: imagen → JSON en una llamada (OCR + parseo + unidades) |
| Selección de modo | **Toggle manual** en captura: "Analizar plato" / "Escanear etiqueta" |
| Cantidad | **Pedir porciones al escanear** (flujo de 2 pasos: leer → porciones → guardar) |
| Unidad de cantidad | **Porciones** (no gramos); default 1, atajo "todo el envase ×N" si la etiqueta lo trae |
| Paso de guardar | El cliente **reenvía los valores extraídos** (no re-sube la foto); confía en el cliente — aceptable para test |
| USDA | **No interviene**: los datos salen de la propia etiqueta |
| Modelo BD | **Sin cambios de esquema**: 1 `DetalleComida` por producto; reusa save + rollup + DTO |
| Restricción | **Modelo gratuito** por ahora (free tier de Gemini) |

## 3. Estado actual relevante (verificado esta sesión)

- `ServicioVisionGoogle` usa `DetectLabels`. El pipeline de "plato" es:
  `OrquestadorAnalisisComida` → filtro → USDA (match/porción/escalado) → traducción → `RegistroComida`.
- `POST /api/comidas/analizar` (foto, tipo, fechaLocal) guarda y devuelve `AnalisisComidaDto`
  (con `DetalleId` por detalle); mapea con el helper privado `ADto(RegistroComida)`.
- `ServicioCorreccionPorciones` + `PUT /api/comidas/{id}/porciones` permiten ajustar después.
- `RegistroComida`/`DetalleComida` (Domain), `ServicioResumenDiario.RecalcularDiaAsync` (rollup).
- No hay LLM en el stack todavía; sí Vision + Cloud Translation + USDA (HttpClients tipados en DI).

## 4. Diseño

### 4.1 Servicio de extracción (backend)

- **Abstracción** `IServicioEtiquetaNutricional` (Application/Abstractions):
  ```csharp
  Task<EtiquetaNutricional?> LeerEtiquetaAsync(Stream imagen, CancellationToken ct = default);
  ```
- **Record** `EtiquetaNutricional` (Application):
  ```
  string? NombreProducto, decimal TamPorcion, string UnidadPorcion ("g"|"mL"),
  decimal? PorcionesPorEnvase,
  decimal CaloriasPorPorcion, decimal ProteinaPorPorcion, decimal CarbosPorPorcion, decimal GrasasPorPorcion
  ```
- **Impl** `ServicioEtiquetaGemini` (Infrastructure): `HttpClient` tipado a
  `https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={ApiKey}`.
  - Cuerpo: la imagen como `inlineData` (base64 + mimeType) + un prompt que pide **solo JSON**
    con `responseMimeType: "application/json"` y un `responseSchema` que fije los campos de arriba.
  - El prompt instruye: devolver **kcal** (convertir kJ→kcal ÷4.184 si la etiqueta está en kJ),
    valores **por porción**, tamaño de porción con su unidad, porciones por envase si aparece,
    y `null`/0 si la imagen no es una etiqueta nutricional legible.
  - Best-effort: cualquier error/respuesta inválida → devuelve `null`.
  - API key en user-secrets `Gemini:ApiKey` (no en repo).

### 4.2 Endpoints (ComidasController, `[Authorize]`)

- **`POST /api/comidas/leer-etiqueta`** — `[FromForm] IFormFile foto`. Llama `LeerEtiquetaAsync`.
  - `null` o sin calorías válidas → `400` "No se pudo leer la etiqueta; enfoca el panel nutricional".
  - Si ok → `200` con `EtiquetaNutricionalDto` (los campos del record). **No guarda nada.**
- **`POST /api/comidas/guardar-etiqueta`** — `[FromBody] GuardarEtiquetaDto`:
  ```
  { nombreProducto, tamPorcion, unidadPorcion, caloriasPorPorcion, proteinaPorPorcion,
    carbosPorPorcion, grasasPorPorcion, porciones (decimal, default 1), tipo, fechaLocal }
  ```
  - Construye `RegistroComida` con **1 `DetalleComida`**: macros = porPorcion × `porciones`,
    `Cantidad` = `porciones` × `tamPorcion`, `UnidadMedida` = `unidadPorcion`,
    `NombreAlimento` = `nombreProducto` (fallback "Producto"), `ConfianzaDeteccion` = 1.
  - Totales del registro = los del único detalle. Guarda en transacción + `RecalcularDiaAsync`.
  - Devuelve `AnalisisComidaDto` vía el `ADto` existente. (No usa `OrquestadorAnalisisComida`.)

### 4.3 Orquestación

- La lógica de construir el `RegistroComida` desde una `EtiquetaNutricional` + porciones es
  pequeña y vive en un servicio testeable `ServicioRegistroEtiqueta` (o método en un servicio de
  Application) para poder probar el escalado por porciones con xUnit. El controller solo enruta y persiste.

### 4.4 Frontend (`CaptureScreen`)

- **Toggle** arriba: "Analizar plato" | "Escanear etiqueta" (estado `modo`).
- **Modo plato**: flujo actual intacto.
- **Modo etiqueta** (2 pasos):
  1. Elegir/tomar foto → botón **"Leer etiqueta"** → `POST /leer-etiqueta` → muestra tarjeta:
     producto, tamaño por porción, macros por porción, porciones por envase.
  2. Campo **"¿Cuántas porciones?"** (default 1; atajo "Todo el envase (×N)" si vino `porcionesPorEnvase`).
  3. Botón **"Guardar"** → `POST /guardar-etiqueta` con los valores leídos + porciones + tipo + fechaLocal
     → muestra el resultado guardado (puede reusar la tarjeta editable de corrección).
- Cliente nuevo en `api/comidas.ts`: `leerEtiqueta(fotoUri, idToken)` y `guardarEtiqueta(payload, idToken)`.

### 4.5 Manejo de errores

- Foto que no es etiqueta / OCR ilegible → mensaje claro (no crash).
- Gemini caído / sin API key → error best-effort; el modo plato sigue funcionando.

## 5. Sin migración de BD

Reusa `RegistroComida`/`DetalleComida`. La corrección manual existente (`PUT .../porciones`)
sigue aplicando al registro creado por etiqueta.

## 6. Prerrequisitos de setup (manual, una vez)

- Crear una **API key gratuita en Google AI Studio** (https://aistudio.google.com/apikey) y
  ponerla en user-secrets del proyecto Api: `Gemini:ApiKey`.
- (El free tier puede usar datos para entrenamiento — aceptable para pruebas.)

## 7. Orden sugerido de implementación

1. Record `EtiquetaNutricional` + `IServicioEtiquetaNutricional` + `ServicioRegistroEtiqueta` (puro/testeable) + tests del escalado.
2. `ServicioEtiquetaGemini` (HttpClient + prompt + parseo) + DI + config.
3. Endpoints `leer-etiqueta` / `guardar-etiqueta` + DTOs.
4. Frontend: toggle + flujo de 2 pasos + cliente API.

## 8. Fuera de alcance

- Detección automática plato/etiqueta (se eligió toggle manual).
- Modelos de pago / Vertex AI (se eligió free tier).
- Traducción del nombre del producto (suele venir ya en el idioma de la etiqueta; se guarda tal cual).
- OAuth nativo / dev build móvil (sesión posterior).
