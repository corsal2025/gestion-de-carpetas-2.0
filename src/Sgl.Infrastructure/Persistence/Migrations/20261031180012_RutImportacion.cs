using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RutImportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RutImportacion",
                table: "Carpetas",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_RutImportacion",
                table: "Carpetas",
                column: "RutImportacion");

            // Backfill only fills the new column; no row is added, removed or moved of estado. A carpeta that came from the agenda
            // listing (it has a taking date) starts with the RUT its citizen has today, the one the listing will bring again.
            migrationBuilder.Sql(
                "UPDATE \"Carpetas\" SET \"RutImportacion\" = " +
                "(SELECT \"Rut\" FROM \"Ciudadanos\" WHERE \"Ciudadanos\".\"Id\" = \"Carpetas\".\"CiudadanoId\") " +
                "WHERE \"FechaTomaCita\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carpetas_RutImportacion",
                table: "Carpetas");

            migrationBuilder.DropColumn(
                name: "RutImportacion",
                table: "Carpetas");
        }
    }
}
