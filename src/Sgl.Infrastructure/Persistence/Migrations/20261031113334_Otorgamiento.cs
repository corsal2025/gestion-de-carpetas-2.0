using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Otorgamiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaOtorgamiento",
                table: "Carpetas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Resolucion",
                table: "Carpetas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_Decision_Estado",
                table: "Carpetas",
                columns: new[] { "Decision", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carpetas_Decision_Estado",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "FechaOtorgamiento",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "Resolucion",
                table: "Carpetas");
        }
    }
}
