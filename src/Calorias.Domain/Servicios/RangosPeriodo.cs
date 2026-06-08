using Calorias.Domain.Entities;

namespace Calorias.Domain.Servicios;

/// <summary>Rango del período actual y del inmediatamente anterior (fechas inclusivas).</summary>
public record RangoComparativo(
    DateOnly ActualInicio,
    DateOnly ActualFin,
    DateOnly AnteriorInicio,
    DateOnly AnteriorFin);

/// <summary>
/// Calcula, para un período y una fecha ancla, el rango [inicio, fin] del período que
/// contiene al ancla y el del período anterior. Puro: sin estado ni dependencias.
/// </summary>
public static class RangosPeriodo
{
    public static RangoComparativo Calcular(Periodo periodo, DateOnly ancla)
    {
        switch (periodo)
        {
            case Periodo.Diario:
            {
                var ant = ancla.AddDays(-1);
                return new RangoComparativo(ancla, ancla, ant, ant);
            }
            case Periodo.Semanal:
            {
                int offsetLunes = ((int)ancla.DayOfWeek + 6) % 7; // domingo=0 -> 6; lunes=1 -> 0
                var ini = ancla.AddDays(-offsetLunes);
                var fin = ini.AddDays(6);
                return new RangoComparativo(ini, fin, ini.AddDays(-7), ini.AddDays(-1));
            }
            case Periodo.Mensual:
            {
                var ini = new DateOnly(ancla.Year, ancla.Month, 1);
                var fin = ini.AddMonths(1).AddDays(-1);
                return new RangoComparativo(ini, fin, ini.AddMonths(-1), ini.AddDays(-1));
            }
            case Periodo.Trimestral:
            {
                int mesInicio = (ancla.Month - 1) / 3 * 3 + 1;
                var ini = new DateOnly(ancla.Year, mesInicio, 1);
                var fin = ini.AddMonths(3).AddDays(-1);
                return new RangoComparativo(ini, fin, ini.AddMonths(-3), ini.AddDays(-1));
            }
            case Periodo.Semestral:
            {
                int mesInicio = ancla.Month <= 6 ? 1 : 7;
                var ini = new DateOnly(ancla.Year, mesInicio, 1);
                var fin = ini.AddMonths(6).AddDays(-1);
                return new RangoComparativo(ini, fin, ini.AddMonths(-6), ini.AddDays(-1));
            }
            case Periodo.Anual:
            {
                var ini = new DateOnly(ancla.Year, 1, 1);
                var fin = new DateOnly(ancla.Year, 12, 31);
                return new RangoComparativo(ini, fin, ini.AddYears(-1), ini.AddDays(-1));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(periodo), periodo, "Período no soportado.");
        }
    }
}
