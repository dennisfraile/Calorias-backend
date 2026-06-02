using Calorias.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Calorias.Infrastructure.Persistence;

public class CaloriasDbContext(DbContextOptions<CaloriasDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RegistroComida> Registros => Set<RegistroComida>();
    public DbSet<DetalleComida> Detalles => Set<DetalleComida>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(255);            // 'sub' de Google
            e.Property(x => x.Email).IsRequired().HasMaxLength(320);
            e.HasIndex(x => x.Email);
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
    }
}
