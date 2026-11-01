using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorreosDescartados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorreosDescartados",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Asunto = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Remitente = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RecibidoEn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DescartadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorreosDescartados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorreosDescartados_DescartadoEn",
                table: "CorreosDescartados",
                column: "DescartadoEn");

            migrationBuilder.CreateIndex(
                name: "IX_CorreosDescartados_MessageId",
                table: "CorreosDescartados",
                column: "MessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorreosDescartados");
        }
    }
}
