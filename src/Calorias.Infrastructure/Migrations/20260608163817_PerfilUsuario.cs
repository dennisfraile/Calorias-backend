using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calorias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PerfilUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlturaCm",
                table: "Usuarios",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaNacimiento",
                table: "Usuarios",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetaCaloriasOverride",
                table: "Usuarios",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetaCarbosPct",
                table: "Usuarios",
                type: "integer",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<int>(
                name: "MetaGrasasPct",
                table: "Usuarios",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "MetaProteinaPct",
                table: "Usuarios",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "NivelActividad",
                table: "Usuarios",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Objetivo",
                table: "Usuarios",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PesoKg",
                table: "Usuarios",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RitmoKgSemana",
                table: "Usuarios",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sexo",
                table: "Usuarios",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlturaCm",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "FechaNacimiento",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "MetaCaloriasOverride",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "MetaCarbosPct",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "MetaGrasasPct",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "MetaProteinaPct",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "NivelActividad",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Objetivo",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "PesoKg",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "RitmoKgSemana",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Sexo",
                table: "Usuarios");
        }
    }
}
