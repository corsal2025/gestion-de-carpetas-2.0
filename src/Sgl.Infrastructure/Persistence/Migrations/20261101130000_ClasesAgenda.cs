using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sgl.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SglDbContext))]
[Migration("20261101130000_ClasesAgenda")]
public sealed class ClasesAgenda : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<int>(name: "ClasesAgenda", table: "Carpetas", type: "INTEGER", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "ClasesAgenda", table: "Carpetas");
}
