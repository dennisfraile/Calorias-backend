# Mejoras al Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir al `DashboardScreen` navegación de fechas (ver períodos pasados) y un panel de detalle por bucket al tocar una barra.

**Architecture:** Solo frontend. El backend (`GET /api/comidas/resumen?periodo=&ancla=`) ya acepta `ancla` y calcula rango actual/anterior; el frontend hoy siempre manda hoy. Añadimos estado `ancla` + helpers puros de shift/formato de fechas, y estado `bucketSeleccionado` para el panel.

**Tech Stack:** Expo SDK 54, React Native 0.81, TypeScript. Sin libs nuevas. Verificación: `frontend\.\node_modules\.bin\tsc --noEmit` (NO `npx tsc`) + `npm run web` para prueba manual.

**Repo:** `frontend` (default branch **master**). Trabajar en rama nueva, PR por la web.

---

### Task 1: Helpers puros de fechas para el dashboard

**Files:**
- Create: `frontend/src/screens/dashboardFechas.ts`

Estos helpers son funciones puras (sin React) para poder razonarlas aisladas; el shift y el formato del rango dependen solo de `(periodo, ancla)`.

- [ ] **Step 1: Crear el archivo con los helpers**

```typescript
import type { Periodo } from '../api/resumen';

/** Fecha local de hoy en formato YYYY-MM-DD (sin desfase de zona). */
export function hoyLocal(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Parsea 'YYYY-MM-DD' a Date local (mediodía para evitar saltos por DST). */
function parse(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d, 12, 0, 0, 0);
}

function fmt(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Desplaza el ancla un período hacia atrás (signo -1) o adelante (+1). */
export function desplazarAncla(ancla: string, periodo: Periodo, signo: -1 | 1): string {
  const d = parse(ancla);
  switch (periodo) {
    case 'diario': d.setDate(d.getDate() + signo); break;
    case 'semanal': d.setDate(d.getDate() + 7 * signo); break;
    case 'mensual': d.setMonth(d.getMonth() + signo); break;
    case 'trimestral': d.setMonth(d.getMonth() + 3 * signo); break;
    case 'semestral': d.setMonth(d.getMonth() + 6 * signo); break;
    case 'anual': d.setFullYear(d.getFullYear() + signo); break;
  }
  return fmt(d);
}

/** True si avanzar (+1) desde `ancla` caería en un período futuro respecto a hoy. */
export function enTopeFuturo(ancla: string, periodo: Periodo): boolean {
  return desplazarAncla(ancla, periodo, 1) > hoyLocal();
}

const MESES = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

/** Etiqueta legible del rango que contiene al ancla, según el período. */
export function etiquetaRango(ancla: string, periodo: Periodo): string {
  const d = parse(ancla);
  const y = d.getFullYear();
  switch (periodo) {
    case 'diario':
      return `${d.getDate()} ${MESES[d.getMonth()]} ${y}`;
    case 'semanal': {
      const ini = new Date(d);
      const offsetLunes = (d.getDay() + 6) % 7; // domingo=0 -> 6 ; lunes=1 -> 0
      ini.setDate(d.getDate() - offsetLunes);
      const fin = new Date(ini);
      fin.setDate(ini.getDate() + 6);
      return `${ini.getDate()} ${MESES[ini.getMonth()]} – ${fin.getDate()} ${MESES[fin.getMonth()]}`;
    }
    case 'mensual':
      return `${MESES[d.getMonth()]} ${y}`;
    case 'trimestral':
      return `T${Math.floor(d.getMonth() / 3) + 1} ${y}`;
    case 'semestral':
      return `S${d.getMonth() < 6 ? 1 : 2} ${y}`;
    case 'anual':
      return `${y}`;
  }
}
```

- [ ] **Step 2: Verificar tipos**

Run: `cd frontend ; .\node_modules\.bin\tsc --noEmit`
Expected: PASS (sin errores). El archivo aún no se usa; solo debe compilar.

- [ ] **Step 3: Commit**

```bash
git -C frontend add src/screens/dashboardFechas.ts
git -C frontend commit -m "feat(dashboard): helpers puros de shift y etiqueta de rango por periodo"
```

---

### Task 2: Cablear navegación de fechas en DashboardScreen

**Files:**
- Modify: `frontend/src/screens/DashboardScreen.tsx`

- [ ] **Step 1: Reemplazar el import de `hoyLocal` y añadir los nuevos helpers**

En la cabecera de imports, eliminar la función local `hoyLocal` (líneas 18-21) y en su lugar importar desde el helper. Cambiar el bloque de imports superior para añadir:

```typescript
import { desplazarAncla, enTopeFuturo, etiquetaRango, hoyLocal } from './dashboardFechas';
```

Y **borrar** la definición local:

```typescript
// BORRAR estas líneas (ya viven en dashboardFechas.ts):
function hoyLocal(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
```

- [ ] **Step 2: Añadir estado `ancla` y usarlo en el fetch**

Dentro de `DashboardScreen`, junto a los otros `useState`, añadir:

```typescript
  const [ancla, setAncla] = useState<string>(hoyLocal());
```

En el `useEffect`, cambiar la llamada `getResumen(auth.idToken, periodo, hoyLocal())` por `getResumen(auth.idToken, periodo, ancla)` y añadir `ancla` a las dependencias del efecto (el array final pasa de `[auth.idToken, periodo]` a `[auth.idToken, periodo, ancla]`).

También, al cambiar de período conviene resetear el bucket seleccionado (ver Task 3); por ahora solo el ancla.

- [ ] **Step 3: Añadir la barra de navegación bajo los tabs**

Justo después del `</View>` que cierra `styles.tabs` (la fila de períodos) y antes del `{cargando && ...}`, insertar:

```tsx
        <View style={styles.navFechas}>
          <Pressable
            onPress={() => setAncla((a) => desplazarAncla(a, periodo, -1))}
            style={({ pressed }) => [styles.navBtn, pressed && styles.pressed]}
          >
            <Text style={styles.navBtnText}>‹</Text>
          </Pressable>
          <Text style={styles.navLabel}>{etiquetaRango(ancla, periodo)}</Text>
          <Pressable
            disabled={enTopeFuturo(ancla, periodo)}
            onPress={() => setAncla((a) => desplazarAncla(a, periodo, 1))}
            style={({ pressed }) => [
              styles.navBtn,
              enTopeFuturo(ancla, periodo) && styles.navBtnDisabled,
              pressed && styles.pressed,
            ]}
          >
            <Text style={styles.navBtnText}>›</Text>
          </Pressable>
          <Pressable
            onPress={() => setAncla(hoyLocal())}
            style={({ pressed }) => [styles.hoyBtn, pressed && styles.pressed]}
          >
            <Text style={styles.hoyBtnText}>Hoy</Text>
          </Pressable>
        </View>
```

- [ ] **Step 4: Añadir los estilos de la barra de navegación**

En `makeStyles`, dentro del `StyleSheet.create({ ... })`, añadir:

```typescript
    navFechas: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.sm },
    navBtn: {
      width: 36, height: 36, borderRadius: radius.md, alignItems: 'center', justifyContent: 'center',
      backgroundColor: colors.surfaceAlt, borderWidth: 1, borderColor: colors.border,
    },
    navBtnDisabled: { opacity: 0.35 },
    navBtnText: { color: colors.text, fontSize: 20, fontWeight: '800', lineHeight: 22 },
    navLabel: { color: colors.text, fontSize: 14, fontWeight: '700', minWidth: 130, textAlign: 'center' },
    hoyBtn: {
      paddingVertical: spacing.xs, paddingHorizontal: spacing.md, borderRadius: radius.md,
      backgroundColor: colors.surfaceAlt, borderWidth: 1, borderColor: colors.border,
    },
    hoyBtnText: { color: colors.textMuted, fontSize: 13, fontWeight: '600' },
```

- [ ] **Step 5: Verificar tipos**

Run: `cd frontend ; .\node_modules\.bin\tsc --noEmit`
Expected: PASS.

- [ ] **Step 6: Verificación manual (web)**

Run: `cd frontend ; npm run web` (con el backend corriendo y sesión iniciada).
Esperado: bajo los tabs aparece `‹ [rango] › Hoy`. `‹` carga el período anterior (los números cambian), `›` se deshabilita cuando el rango es el actual, "Hoy" vuelve al período de hoy. Cambiar de tab mantiene coherente la etiqueta.

- [ ] **Step 7: Commit**

```bash
git -C frontend add src/screens/DashboardScreen.tsx
git -C frontend commit -m "feat(dashboard): navegacion de fechas (anterior/siguiente/hoy) con tope futuro"
```

---

### Task 3: Panel de detalle por bucket (tap)

**Files:**
- Modify: `frontend/src/screens/DashboardScreen.tsx`

- [ ] **Step 1: Añadir estado de bucket seleccionado y resetearlo al cambiar período/ancla**

Junto a los `useState`, añadir:

```typescript
  const [bucketSel, setBucketSel] = useState<number | null>(null);
```

En el `useEffect` (que ya depende de `auth.idToken, periodo, ancla`), al inicio del cuerpo añadir `setBucketSel(null);` para limpiar la selección cuando cambian los datos.

- [ ] **Step 2: Pasar la selección al componente `Barras` y hacer las barras pulsables**

Cambiar el render de `Barras` para pasarle la selección y el setter:

```tsx
            <Barras
              data={data}
              colors={colors}
              styles={styles}
              seleccion={bucketSel}
              onSeleccion={(i) => setBucketSel((prev) => (prev === i ? null : i))}
            />
```

Actualizar la firma de `Barras` y envolver cada `barPair` en un `Pressable`:

```tsx
function Barras({
  data,
  colors,
  styles,
  seleccion,
  onSeleccion,
}: {
  data: ResumenPeriodos;
  colors: Palette;
  styles: Estilos;
  seleccion: number | null;
  onSeleccion: (i: number) => void;
}) {
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
              <Pressable key={i} onPress={() => onSeleccion(i)} style={styles.barPair}>
                <View style={[styles.barAnterior, { height: hp }]} />
                <View
                  style={[
                    styles.barActual,
                    { height: ha },
                    seleccion === i && { backgroundColor: colors.accent },
                  ]}
                />
              </Pressable>
            );
          })}
        </View>
      </View>
      <View style={styles.labelsRow}>
        {actual.buckets.map((b, i) => (
          <Text
            key={i}
            style={[styles.barLabel, seleccion === i && { color: colors.text, fontWeight: '700' }]}
            numberOfLines={1}
          >
            {b.etiqueta}
          </Text>
        ))}
      </View>
      <View style={styles.legend}>
        <Leyenda color={colors.primary} text="Actual" styles={styles} />
        <Leyenda color={colors.textMuted} text="Anterior" styles={styles} />
        {meta && <Leyenda color={colors.accent} text="Meta" styles={styles} />}
      </View>
      {seleccion != null && actual.buckets[seleccion] && (
        <BucketDetalle
          actual={actual.buckets[seleccion]}
          anterior={anterior.buckets[seleccion]}
          styles={styles}
        />
      )}
    </View>
  );
}
```

- [ ] **Step 3: Añadir el componente `BucketDetalle`**

Después de la función `Barras`, añadir:

```tsx
function BucketDetalle({
  actual,
  anterior,
  styles,
}: {
  actual: BucketResumen;
  anterior?: BucketResumen;
  styles: Estilos;
}) {
  return (
    <View style={styles.detalleBucket}>
      <Text style={styles.detalleBucketTitulo}>{actual.etiqueta}</Text>
      <View style={styles.detalleBucketRow}>
        <Text style={styles.detalleBucketKcal}>{actual.kcal} kcal</Text>
        {anterior && <Text style={styles.detalleBucketAnt}>(anterior: {anterior.kcal} kcal)</Text>}
      </View>
      <Text style={styles.detalleBucketMacros}>
        P {actual.proteinaG} g · C {actual.carbosG} g · G {actual.grasasG} g
      </Text>
    </View>
  );
}
```

Añadir `BucketResumen` al import de tipos desde `../api/resumen`:

```typescript
import { getResumen, type BucketResumen, type Periodo, type ResumenPeriodos } from '../api/resumen';
```

- [ ] **Step 4: Añadir estilos del panel de detalle**

En `makeStyles`, añadir:

```typescript
    detalleBucket: {
      marginTop: spacing.sm, paddingTop: spacing.sm,
      borderTopWidth: 1, borderTopColor: colors.border, gap: 2,
    },
    detalleBucketTitulo: { color: colors.text, fontSize: 14, fontWeight: '700' },
    detalleBucketRow: { flexDirection: 'row', alignItems: 'baseline', gap: spacing.xs },
    detalleBucketKcal: { color: colors.primary, fontSize: 18, fontWeight: '800' },
    detalleBucketAnt: { color: colors.textMuted, fontSize: 12 },
    detalleBucketMacros: { color: colors.textMuted, fontSize: 13 },
```

- [ ] **Step 5: Verificar tipos**

Run: `cd frontend ; .\node_modules\.bin\tsc --noEmit`
Expected: PASS.

- [ ] **Step 6: Verificación manual (web)**

Esperado: tocar una barra la resalta (color accent) y abre un panel debajo con la etiqueta del bucket, kcal actual (y anterior entre paréntesis) y la línea de macros P/C/G. Tocarla de nuevo cierra el panel. Cambiar de período/fecha limpia la selección.

- [ ] **Step 7: Commit**

```bash
git -C frontend add src/screens/DashboardScreen.tsx
git -C frontend commit -m "feat(dashboard): panel de detalle por bucket al tocar una barra"
```

---

## Cierre

- [ ] Verificar `tsc --noEmit` limpio.
- [ ] Verificación manual web de ambas funciones.
- [ ] Usar `superpowers:finishing-a-development-branch` para abrir PR contra `master` (PR por la web; el usuario mergea).
