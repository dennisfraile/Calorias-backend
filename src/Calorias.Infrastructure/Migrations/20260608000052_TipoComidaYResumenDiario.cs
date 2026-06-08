using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calorias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TipoComidaYResumenDiario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Registros_UsuarioId",
                table: "Registros");

            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaLocal",
                table: "Registros",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                table: "Registros",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ResumenesDiarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<string>(type: "text", nullable: false),
                    FechaLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    CaloriasTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ProteinasTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CarbosTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    GrasasTotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    NumComidas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResumenesDiarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Registros_UsuarioId_FechaLocal",
                table: "Registros",
                columns: new[] { "UsuarioId", "FechaLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_ResumenesDiarios_UsuarioId_FechaLocal",
                table: "ResumenesDiarios",
                columns: new[] { "UsuarioId", "FechaLocal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResumenesDiarios");

            migrationBuilder.DropIndex(
                name: "IX_Registros_UsuarioId_FechaLocal",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "FechaLocal",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "Registros");

            migrationBuilder.CreateIndex(
                name: "IX_Registros_UsuarioId",
                table: "Registros",
                column: "UsuarioId");
        }
    }
}
