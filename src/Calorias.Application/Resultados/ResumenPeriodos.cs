namespace Calorias.Application.Resultados;

public record MacrosResumen(int ProteinaG, int CarbosG, int GrasasG);

public record BucketResumen(string Etiqueta, int Kcal, int ProteinaG, int CarbosG, int GrasasG);

public record LadoPeriodo(
    int PromedioKcalDia,
    int? DeficitMedioVsMeta,
    MacrosResumen Macros,
    IReadOnlyList<BucketResumen> Buckets);

public record MetaResumen(int Calorias, int ProteinaG, int CarbosG, int GrasasG);

public record ResumenPeriodos(
    string Periodo,
    LadoPeriodo Actual,
    LadoPeriodo Anterior,
    double? CambioPct,
    MetaResumen? Meta);
