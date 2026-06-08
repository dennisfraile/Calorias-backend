using Calorias.Application.Abstractions;
using Calorias.Infrastructure.Persistence;
using Calorias.Infrastructure.Servicios;
using Google.Cloud.Vision.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Calorias.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // PostgreSQL + EF Core
        services.AddDbContext<CaloriasDbContext>(o =>
            o.UseNpgsql(config.GetConnectionString("Postgres")));

        // Google Vision: el SDK lee la credencial desde GOOGLE_APPLICATION_CREDENTIALS
        services.AddSingleton(_ => ImageAnnotatorClient.Create());
        services.AddScoped<IServicioVision, ServicioVisionGoogle>();

        // USDA FoodData Central: HttpClient tipado; la API key va en el header X-Api-Key.
        // (api.data.gov acepta "DEMO_KEY" con límites bajos si no hay clave configurada.)
        services.AddHttpClient<IServicioNutricion, ServicioNutricionUsda>(c =>
        {
            c.BaseAddress = new Uri("https://api.nal.usda.gov/fdc/");
            c.DefaultRequestHeaders.Add("X-Api-Key", config["Usda:ApiKey"] ?? "DEMO_KEY");
        });

        services.AddScoped<IServicioUsuarios, ServicioUsuarios>();
        services.AddScoped<IServicioResumenDiario, ServicioResumenDiario>();
        services.AddScoped<IServicioResumenPeriodos, ServicioResumenPeriodos>();

        return services;
    }
}
