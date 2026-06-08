using Calorias.Domain.Entities;

namespace Calorias.Domain.Servicios;

/// <summary>Resultado del cálculo de objetivos: meta calórica, banderas y macros en gramos.</summary>
public record ResultadoObjetivos(
    int MetaCalorias,
    bool MetaTopada,
    int ProteinaG,
    int CarbosG,
    int GrasasG,
    int Tmb,
    int Tdee);

/// <summary>
/// Cálculo de objetivos calóricos (Mifflin-St Jeor → TDEE → ajuste por objetivo → piso de
/// seguridad → override). Servicio puro: sin estado ni dependencias, totalmente unit-testeable.
/// </summary>
public static class CalculadoraObjetivos
{
    private const decimal KcalPorKgGrasa = 7700m;

    public static decimal FactorActividad(NivelActividad nivel) => nivel switch
    {
        NivelActividad.Sedentario => 1.2m,
        NivelActividad.Ligero     => 1.375m,
        NivelActividad.Moderado   => 1.55m,
        NivelActividad.Activo     => 1.725m,
        NivelActividad.MuyActivo  => 1.9m,
        _                         => 1.2m,
    };

    public static ResultadoObjetivos Calcular(
        Sexo sexo, int edad, int alturaCm, decimal pesoKg,
        NivelActividad nivel, Objetivo objetivo, decimal ritmoKgSemana,
        int? metaCaloriasOverride, int proteinaPct, int carbosPct, int grasasPct)
    {
        // TMB (Mifflin-St Jeor)
        decimal tmb = 10m * pesoKg + 6.25m * alturaCm - 5m * edad
                      + (sexo == Sexo.Masculino ? 5m : -161m);

        decimal tdee = tmb * FactorActividad(nivel);

        // Ajuste por objetivo: 1 kg grasa ≈ 7700 kcal; ajuste diario = ritmo*7700/7.
        decimal ajuste = ritmoKgSemana * KcalPorKgGrasa / 7m;
        decimal objetivoCal = objetivo switch
        {
            Objetivo.Perder => tdee - ajuste,
            Objetivo.Ganar  => tdee + ajuste,
            _               => tdee, // Mantener
        };

        // Piso de seguridad: nunca por debajo de la TMB ni del mínimo por sexo.
        decimal piso = Math.Max(tmb, sexo == Sexo.Masculino ? 1500m : 1200m);
        bool topada = metaCaloriasOverride is null && objetivoCal < piso;
        decimal calculada = Math.Max(objetivoCal, piso);

        int metaFinal = metaCaloriasOverride
                        ?? (int)Math.Round(calculada, MidpointRounding.AwayFromZero);

        int Gramos(int pct, int kcalPorGramo) =>
            (int)Math.Round(pct / 100m * metaFinal / kcalPorGramo, MidpointRounding.AwayFromZero);

        return new ResultadoObjetivos(
            MetaCalorias: metaFinal,
            MetaTopada: topada,
            ProteinaG: Gramos(proteinaPct, 4),
            CarbosG:   Gramos(carbosPct, 4),
            GrasasG:   Gramos(grasasPct, 9),
            Tmb:  (int)Math.Round(tmb, MidpointRounding.AwayFromZero),
            Tdee: (int)Math.Round(tdee, MidpointRounding.AwayFromZero));
    }
}
