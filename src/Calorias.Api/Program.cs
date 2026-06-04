using Calorias.Application.Servicios;
using Calorias.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Capas de la aplicación ---
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<OrquestadorAnalisisComida>();

// --- Autenticación con Google (flujo ID Token) ---
// El 'aud' del token es el Client ID de la plataforma que lo emitió (web/iOS/Android),
// así que aceptamos varias audiencias. Compatibilidad:
//   - Google:ClientId            -> una sola audiencia (modo simple)
//   - Google:ValidAudiences:0..n -> lista (uno por plataforma)
var audienciasGoogle = builder.Configuration
    .GetSection("Google:ValidAudiences").Get<string[]>() ?? [];
var clientIdSimple = builder.Configuration["Google:ClientId"];
var audiencias = audienciasGoogle
    .Append(clientIdSimple)
    .Where(a => !string.IsNullOrWhiteSpace(a))
    .Select(a => a!)
    .Distinct()
    .ToArray();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Google es el emisor; validamos su firma con sus claves públicas.
        options.Authority = "https://accounts.google.com";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "https://accounts.google.com",
            ValidAudiences = audiencias,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
        };
    });

builder.Services.AddAuthorization();

// --- CORS (necesario para el cliente Expo web en el navegador) ---
// El front web corre en localhost:8081 y llama a este backend en otro puerto:
// es cross-origin. Se envía Authorization (no cookies), así que basta con
// permitir el origen + cualquier header/método. (En nativo CORS no aplica.)
const string CorsDevWeb = "dev-web";
builder.Services.AddCors(o => o.AddPolicy(CorsDevWeb, p => p
    .WithOrigins("http://localhost:8081", "http://localhost:19006")
    .AllowAnyHeader()
    .AllowAnyMethod()));

// --- MVC / OpenAPI ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(CorsDevWeb);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
