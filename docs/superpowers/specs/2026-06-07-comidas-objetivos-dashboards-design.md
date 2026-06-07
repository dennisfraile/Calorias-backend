# Diseño: tipos de comida, objetivos calóricos y dashboards comparativos

- **Fecha:** 2026-06-07
- **Estado:** Aprobado (pendiente de plan de implementación)
- **Repos afectados:** `backend` (.NET 10, Clean Architecture) y `frontend` (Expo/Mythik, target web)

## 1. Objetivo

Ampliar la app de seguimiento de calorías para que el usuario pueda:

1. Etiquetar cada comida como **desayuno / almuerzo / cena / snack**.
2. Obtener una **meta de calorías diarias** calculada con datos profesionales (TDEE
   por Mifflin-St Jeor) y editable, con metas completas de **macros**.
3. Ver **dashboards comparativos** por período (diario, semanal, mensual, trimestral,
   semestral, anual) con la métrica **promedio kcal/día vs meta y vs período anterior**.

## 2. Decisiones tomadas (brainstorming)

| Tema | Decisión |
|---|---|
| Origen de la meta | Calcular TDEE (Mifflin-St Jeor) desde perfil físico + **permitir override** |
| Macros | **Metas completas** de proteína/carbos/grasas (no solo informativas) |
| Métrica principal | Promedio kcal/día vs meta y vs período anterior (% cambio) |
| Layout dashboard | **B — comparativa lado a lado** (actual vs anterior, barras intercaladas) |
| Arquitectura datos | **Enfoque 3**: pre-agregación con rollup diario + agregación por `GROUP BY` |
| Buckets en "Diario" | Por comida (desayuno/almuerzo/cena/snack) |
| Navegación | 3 tabs (Captura/Historial/Dashboard) + **icono de Perfil en la esquina** |

## 3. Modelo de datos

### 3.1 Entidades existentes (modificadas)

**`RegistroComida`** (+ campos):
- `TipoComida Tipo` — enum **obligatorio**.
- `DateOnly FechaLocal` — día local del usuario (lo envía el cliente al capturar).
  Los agrupamientos "por día" usan `FechaLocal`, no `FechaRegistro` (UTC), para que
  una comida de las 23:00 no caiga en el día equivocado.

**`Usuario`** (+ campos de perfil, nullables hasta completar onboarding):
- `Sexo? Sexo` (enum M/F), `DateOnly? FechaNacimiento`, `int? AlturaCm`, `decimal? PesoKg`,
  `NivelActividad? NivelActividad` (enum), `Objetivo? Objetivo` (enum Perder/Mantener/Ganar),
  `decimal? RitmoKgSemana`.
- `int? MetaCaloriasOverride` — si está, manda sobre el cálculo.
- `int MetaProteinaPct = 30`, `int MetaCarbosPct = 40`, `int MetaGrasasPct = 30`.

### 3.2 Enums (Domain)

- `TipoComida { Desayuno, Almuerzo, Cena, Snack }`
- `Sexo { Masculino, Femenino }`
- `NivelActividad { Sedentario, Ligero, Moderado, Activo, MuyActivo }`
- `Objetivo { Perder, Mantener, Ganar }`

### 3.3 Tabla nueva — rollup `ResumenDiario`

| Columna | Tipo | Nota |
|---|---|---|
| Id | Guid | PK |
| UsuarioId | string | FK → Usuario |
| FechaLocal | date | — |
| CaloriasTotal | decimal | suma del día |
| ProteinasTotal | decimal | — |
| CarbosTotal | decimal | — |
| GrasasTotal | decimal | — |
| NumComidas | int | — |

- Índice **único** `(UsuarioId, FechaLocal)`.
- Única granularidad mantenida incrementalmente. Períodos más gruesos se calculan con
  `GROUP BY` sobre esta tabla al consultar (pocas filas-día → trivial).

### 3.4 Fuera de alcance

- Historial de peso en el tiempo (el TDEE usa `PesoKg` actual; al actualizarlo se
  recalcula la meta). Ampliable luego como `RegistroPeso` sin tocar lo demás.

## 4. Cálculo de objetivos — `CalculadoraObjetivos` (servicio puro de dominio)

**TMB (Mifflin-St Jeor):**
- ♂: `10·peso + 6.25·altura − 5·edad + 5`
- ♀: `10·peso + 6.25·altura − 5·edad − 161`

**TDEE = TMB × factor de actividad:**

| Nivel | Factor |
|---|---|
| Sedentario | 1.2 |
| Ligero | 1.375 |
| Moderado | 1.55 |
| Activo | 1.725 |
| MuyActivo | 1.9 |

**Ajuste por objetivo** (1 kg grasa ≈ 7700 kcal; ajuste diario = `RitmoKgSemana·7700/7`):
- Perder: `TDEE − ajuste`
- Mantener: `TDEE`
- Ganar: `TDEE + ajuste`

**Piso de seguridad:** nunca por debajo de la TMB ni del mínimo **1500 (♂) / 1200 (♀)**.
Si el ajuste lo cruza, se topa ahí y se marca una bandera de aviso.

**Meta final** = `MetaCaloriasOverride` si está; si no, el valor calculado (topado).

**Macros (gramos)** = `pct · metaFinal / k`, con k = 4 (proteína), 4 (carbos), 9 (grasas).

**Lecturas de déficit** (mostradas sin confundir):
- *Planificado*: `metaFinal − TDEE`.
- *Real diario*: `ingesta − metaFinal`.

## 5. Sincronización del rollup

En cada **crear / editar / borrar** de `RegistroComida`, dentro de la **misma transacción**
se recalcula el total del día `(UsuarioId, FechaLocal)` a partir de los registros reales y
se hace **upsert** en `ResumenDiario` (estrategia *recompute-the-day*: idempotente, nunca
se desincroniza). Si el día queda sin comidas, se elimina la fila del rollup.

## 6. API

### 6.1 `GET /api/perfil`
Respuesta: perfil actual + meta calculada + `perfilCompleto: bool`.
```json
{
  "perfilCompleto": true,
  "sexo": "Masculino", "fechaNacimiento": "1995-04-10", "alturaCm": 178,
  "pesoKg": 80, "nivelActividad": "Moderado", "objetivo": "Perder", "ritmoKgSemana": 0.5,
  "metaCalorias": 2050, "metaTopada": false,
  "macros": { "proteinaG": 154, "carbosG": 205, "grasasG": 68 }
}
```

### 6.2 `PUT /api/perfil`
Body: campos de perfil/objetivo/override/split. Devuelve la meta recalculada (misma forma que 6.1).

### 6.3 `POST /api/comidas/analizar` (modificado)
- Multipart: `foto` (igual que hoy) + `tipo` (TipoComida) + `fechaLocal` (YYYY-MM-DD).
- Tras guardar, actualiza `ResumenDiario`.
- Respuesta: `AnalisisComidaDto` (ya existente) + `tipo` y `fechaLocal`.

### 6.4 `GET /api/comidas/resumen?periodo={...}&ancla=YYYY-MM-DD`
`periodo ∈ {diario, semanal, mensual, trimestral, semestral, anual}`. Devuelve período del
`ancla` **y** el anterior, con misma forma para los 6:
```json
{
  "periodo": "semanal",
  "actual":   { "promedioKcalDia": 1850, "deficitMedioVsMeta": -200,
                "macros": { "proteinaG": 120, "carbosG": 190, "grasasG": 60 },
                "buckets": [ { "etiqueta": "L", "kcal": 1700, "proteinaG": 110, "carbosG": 180, "grasasG": 55 } ] },
  "anterior": { "promedioKcalDia": 2010, "deficitMedioVsMeta": -40, "macros": { "...": "igual forma que actual" }, "buckets": ["...igual forma que actual"] },
  "cambioPct": -8.0,
  "meta": { "calorias": 2050, "proteinaG": 154, "carbosG": 205, "grasasG": 68 }
}
```
Granularidad de buckets: Diario → por comida; Semanal → 7 días; Mensual → días; Trimestral
→ 3 meses; Semestral → 6 meses; Anual → 12 meses. Todos vía `GROUP BY` sobre `ResumenDiario`
(el Diario agrupa `RegistroComida.Tipo` del día).

### 6.5 Seguridad y zona horaria
- Todos los endpoints `[Authorize]`; el `UsuarioId` sale del claim `sub` (igual que hoy).
- El cliente envía `fechaLocal` para fijar el día local sin depender del UTC del servidor.

## 7. Frontend

Todo en **RN clásico** (formularios/charts imperativos). Mythik se mantiene para listas
data-driven (historial). Reutiliza el `theme` existente.

- **Navegación (`App.tsx`):** 3 tabs (Captura/Historial/Dashboard) + **icono/avatar de Perfil**
  en la esquina superior derecha que abre la pantalla Perfil.
- **Captura (modificada):** 4 chips de tipo de comida (obligatorio). Envía `tipo` + `fechaLocal`.
- **Perfil (nueva):** formulario (sexo, fecha nac., altura, peso, actividad, objetivo, ritmo;
  split de macros; override kcal). Muestra la meta calculada en vivo. Aviso si el perfil está
  incompleto.
- **Dashboard (nueva) — layout B:** tabs de período; dos cifras enfrentadas (actual vs anterior)
  + pill `▼/▲ %` y déficit vs meta; **barras intercaladas** (actual color, anterior gris) con
  **línea de meta** dibujadas con `View`s (sin librería de charts); sección de macros vs meta.
  Diario → barras por comida. Estados carga/vacío/error.

## 8. Testing

**Backend (unidad):**
- `CalculadoraObjetivos`: tabla de casos (Mifflin ♂/♀, cada factor, los 3 objetivos, piso de
  seguridad, override).
- Rollup `ResumenDiario`: total del día = suma real tras crear/editar/borrar (incluye borrar
  la última comida del día → fila eliminada).
- Bucketing de `resumen`: buckets y "período anterior" en los rangos correctos (bordes de
  semana/mes/año).

**Frontend:** verificación manual por web + `tsc --noEmit`. Sin suite RN (fuera de alcance).

## 9. Fases de implementación

- **Fase A — Cimientos de datos:** `TipoComida` + `FechaLocal`, tabla `ResumenDiario` + sync,
  selector de tipo en Captura, migración EF. *Verificable:* capturar llena el rollup.
- **Fase B — Perfil y metas:** campos de perfil en `Usuario`, `CalculadoraObjetivos`,
  `GET/PUT /api/perfil`, pantalla Perfil + icono. *Verificable:* perfil → meta correcta.
- **Fase C — Dashboard:** `GET /api/comidas/resumen` (6 períodos + comparación) y pantalla
  Dashboard (layout B). *Verificable:* dashboard muestra actual vs anterior con datos reales.

Cada fase deja la app funcionando y se prueba sola.
