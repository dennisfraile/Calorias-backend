using Calorias.Application.Servicios;
using Calorias.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Capas de la aplicación ---
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<OrquestadorAnalisisComida>();

// --- Autenticación con Google (flujo ID Token) ---
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Google es el emisor; validamos su firma con sus claves públicas.
        options.Authority = "https://accounts.google.com";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "https://accounts.google.com",
            // El 'aud' del token debe ser tu Google OAuth Client ID (cliente móvil).
            ValidAudience = builder.Configuration["Google:ClientId"],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
        };
    });

builder.Services.AddAuthorization();

// --- MVC / OpenAPI ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
