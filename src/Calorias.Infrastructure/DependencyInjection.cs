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

        // Nutritionix: HttpClient tipado con headers de auth
        services.AddHttpClient<IServicioNutricion, ServicioNutritionix>(c =>
        {
            c.BaseAddress = new Uri("https://trackapi.nutritionix.com/");
            c.DefaultRequestHeaders.Add("x-app-id",  config["Nutritionix:AppId"]);
            c.DefaultRequestHeaders.Add("x-app-key", config["Nutritionix:AppKey"]);
        });

        services.AddScoped<IServicioUsuarios, ServicioUsuarios>();

        return services;
    }
}
