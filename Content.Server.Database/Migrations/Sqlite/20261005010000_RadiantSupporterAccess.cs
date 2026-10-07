using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

[DbContext(typeof(SqliteServerDbContext))]
[Migration("20261005010000_RadiantSupporterAccess")]
public sealed class RadiantSupporterAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>("supporter_role_id", "radiant_discord_link",
            type: "TEXT", maxLength: 20, nullable: false, defaultValue: "");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn("supporter_role_id", "radiant_discord_link");
}
