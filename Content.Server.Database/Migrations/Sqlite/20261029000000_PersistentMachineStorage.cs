using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

[DbContext(typeof(SqliteServerDbContext))]
[Migration("20261029000000_PersistentMachineStorage")]
public sealed class PersistentMachineStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateTable("radiant_machine_storage", columns: table => new
        {
            storage_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
            kind = table.Column<string>(type: "TEXT", nullable: false),
            payload = table.Column<string>(type: "TEXT", nullable: false),
            revision = table.Column<long>(type: "INTEGER", nullable: false),
        }, constraints: table => table.PrimaryKey("pk_radiant_machine_storage", row => row.storage_id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("radiant_machine_storage");
}
