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
