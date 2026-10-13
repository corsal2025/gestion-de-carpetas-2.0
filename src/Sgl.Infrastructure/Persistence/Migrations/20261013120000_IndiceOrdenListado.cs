using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sgl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndiceOrdenListado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Carpetas_FechaCitacion_Id",
                table: "Carpetas",
                columns: new[] { "FechaCitacion", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carpetas_FechaCitacion_Id",
                table: "Carpetas");
        }
    }
}
