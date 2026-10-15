using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegistroImportaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportacionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Usuario = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FilasLeidas = table.Column<int>(type: "INTEGER", nullable: false),
                    CarpetasCreadas = table.Column<int>(type: "INTEGER", nullable: false),
                    CarpetasActualizadas = table.Column<int>(type: "INTEGER", nullable: false),
                    Errores = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportacionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportacionLogs_Fecha",
                table: "ImportacionLogs",
                column: "Fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportacionLogs");
        }
    }
}
