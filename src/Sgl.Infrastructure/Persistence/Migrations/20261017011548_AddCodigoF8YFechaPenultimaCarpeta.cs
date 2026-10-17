using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCodigoF8YFechaPenultimaCarpeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoF8",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaPenultimaCarpeta",
                table: "Carpetas",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodigoF8",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "FechaPenultimaCarpeta",
                table: "Carpetas");
        }
    }
}
