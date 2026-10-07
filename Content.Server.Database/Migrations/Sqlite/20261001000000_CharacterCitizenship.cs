using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

[DbContext(typeof(SqliteServerDbContext))]
[Migration("20261001000000_CharacterCitizenship")]
public sealed class CharacterCitizenship : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>("citizenship", "profile", type: "TEXT", nullable: false,
            defaultValue: "Confederation");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn("citizenship", "profile");
}
