using Calorias.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Infrastructure.Persistence;

public class CaloriasDbContext(DbContextOptions<CaloriasDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RegistroComida> Registros => Set<RegistroComida>();
    public DbSet<DetalleComida> Detalles => Set<DetalleComida>();
    public DbSet<ResumenDiario> ResumenesDiarios => Set<ResumenDiario>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(255);            // 'sub' de Google
            e.Property(x => x.Email).IsRequired().HasMaxLength(320);
            e.HasIndex(x => x.Email);

            // --- Perfil físico ---
            // Enums nullables como texto legible en la BD.
            e.Property(x => x.Sexo).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.NivelActividad).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Objetivo).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.PesoKg).HasPrecision(5, 2);
            e.Property(x => x.RitmoKgSemana).HasPrecision(4, 2);
            // Split de macros: NOT NULL con default (las filas existentes quedan en 30/40/30).
            e.Property(x => x.MetaProteinaPct).HasDefaultValue(30);
            e.Property(x => x.MetaCarbosPct).HasDefaultValue(40);
            e.Property(x => x.MetaGrasasPct).HasDefaultValue(30);
        });

        b.Entity<RegistroComida>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CaloriasTotales).HasPrecision(10, 2);
            e.Property(x => x.ProteinasTotales).HasPrecision(10, 2);
            e.Property(x => x.CarbohidratosTotales).HasPrecision(10, 2);
            e.Property(x => x.GrasasTotales).HasPrecision(10, 2);

            // Payloads crudos: JSON literal almacenado en columnas jsonb de PostgreSQL
            e.Property(x => x.PayloadVisionJson).HasColumnType("jsonb");
            e.Property(x => x.PayloadNutritionixJson).HasColumnType("jsonb");

            // Enum como texto legible en la BD.
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            // Acelera el recompute-the-day y las consultas por día.
            e.HasIndex(x => new { x.UsuarioId, x.FechaLocal });

            e.HasOne(x => x.Usuario)
             .WithMany(u => u.Registros)
             .HasForeignKey(x => x.UsuarioId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Detalles)
             .WithOne(d => d.RegistroComida)
             .HasForeignKey(d => d.RegistroComidaId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DetalleComida>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Cantidad).HasPrecision(10, 2);
            e.Property(x => x.Calorias).HasPrecision(10, 2);
            e.Property(x => x.Proteinas).HasPrecision(10, 2);
            e.Property(x => x.Carbohidratos).HasPrecision(10, 2);
            e.Property(x => x.Grasas).HasPrecision(10, 2);
        });

        b.Entity<ResumenDiario>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UsuarioId, x.FechaLocal }).IsUnique();
            e.Property(x => x.UsuarioId).HasMaxLength(255);
            e.Property(x => x.CaloriasTotal).HasPrecision(10, 2);
            e.Property(x => x.ProteinasTotal).HasPrecision(10, 2);
            e.Property(x => x.CarbosTotal).HasPrecision(10, 2);
            e.Property(x => x.GrasasTotal).HasPrecision(10, 2);
        });
    }
}
