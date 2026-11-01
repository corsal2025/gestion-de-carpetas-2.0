using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sgl.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SglDbContext))]
[Migration("20261101120000_RespaldosPenultima")]
public sealed class RespaldosPenultima : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "FechaRespaldoSolicitado", table: "Carpetas", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "FechaRespaldoSubido", table: "Carpetas", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FechaRespaldoSolicitado", table: "Carpetas");
        migrationBuilder.DropColumn(name: "FechaRespaldoSubido", table: "Carpetas");
    }
}
