# Diseño: mejoras al Dashboard (navegación de fechas + detalle por bucket)

- **Fecha:** 2026-06-09
- **Estado:** Aprobado (pendiente de plan de implementación)
- **Repos afectados:** `frontend` (Expo/Mythik, target web). **Backend sin cambios** (ya soporta `ancla`).

## 1. Objetivo

Pulir el `DashboardScreen` (Fase C) con dos mejoras pedidas:

1. **Navegación de fechas**: poder ver períodos pasados (ancla ≠ hoy), hoy el frontend siempre manda hoy.
2. **Detalle por bucket**: ver kcal + macros de cada bucket (los macros ya viajan en el DTO, no se muestran).

## 2. Estado actual (verificado contra código)

- Backend `GET /api/comidas/resumen?periodo=&ancla=` **ya acepta `ancla`** y `RangosPeriodo.Calcular`
  computa el rango actual/anterior alrededor de cualquier ancla. Nada que cambiar en backend.
- Frontend `DashboardScreen.tsx` siempre llama `getResumen(token, periodo, hoyLocal())` → **no hay UI de fechas**.
- `BucketResumen` ya trae `{ etiqueta, kcal, proteinaG, carbosG, grasasG }`; la gráfica solo dibuja `kcal`.

## 3. Diseño

### 3.1 Navegación de fechas (2A)

- Barra bajo los tabs de período: `‹  [etiqueta del rango]  ›` + botón **"Hoy"**.
- Estado nuevo `ancla` en `DashboardScreen` (default = hoy local). El `useEffect` ya re-fetchea al cambiar
  `ancla` (se añade a las deps).
- `‹` / `›` desplazan el ancla según el período mediante un helper **puro** de shift:
  - diario −/+ 1 día · semanal −/+ 7 días · mensual −/+ 1 mes · trimestral −/+ 3 meses ·
    semestral −/+ 6 meses · anual −/+ 1 año.
- **Tope futuro**: no navegar más allá del período que contiene hoy; `›` deshabilitado en ese tope.
- **Etiqueta legible** del rango por período (ej. "9–15 jun", "Junio 2026", "T2 2026", "2026"),
  derivada del ancla + período (helper puro de formato).
- "Hoy" resetea `ancla` al día local actual.

### 3.2 Detalle por bucket (2B)

- Las barras (`barPair`) pasan a ser `Pressable`; tocar una fija `bucketSeleccionado` (índice) en estado.
- **Panel** debajo de la gráfica: etiqueta del bucket + kcal (actual vs anterior) + macros P/C/G
  (datos ya presentes en `actual.buckets[i]` y `anterior.buckets[i]`).
- Tocar de nuevo la misma barra o un ✕ cierra el panel. Resalte visual de la barra activa.
- Todo con primitivos existentes (`View`/`Text`/`Pressable`) y la paleta `useTheme`.

## 4. Sin cambios de contrato / backend

No cambia el DTO ni el endpoint. Solo frontend. `tsc --noEmit` debe quedar limpio.

## 5. Fuera de alcance

- Cambios al backend del resumen (ya soporta lo necesario).
- Exportar / compartir dashboard.
