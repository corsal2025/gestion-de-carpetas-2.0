using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgendaCsvYCanceladas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailContacto",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaTomaCita",
                table: "Carpetas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelefonoContacto",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CitasCanceladas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FechaCita = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FechaTomaCita = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HoraCita = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    Sede = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Tramite = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    RegistradaEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CitasCanceladas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_CiudadanoId_FechaTomaCita",
                table: "Carpetas",
                columns: new[] { "CiudadanoId", "FechaTomaCita" });

            migrationBuilder.CreateIndex(
                name: "IX_CitasCanceladas_FechaCita",
                table: "CitasCanceladas",
                column: "FechaCita");

            migrationBuilder.CreateIndex(
                name: "IX_CitasCanceladas_FechaTomaCita_FechaCita_HoraCita_Sede_Tramite",
                table: "CitasCanceladas",
                columns: new[] { "FechaTomaCita", "FechaCita", "HoraCita", "Sede", "Tramite" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CitasCanceladas");

            migrationBuilder.DropIndex(
                name: "IX_Carpetas_CiudadanoId_FechaTomaCita",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "EmailContacto",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "FechaTomaCita",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "TelefonoContacto",
                table: "Carpetas");
        }
    }
}
