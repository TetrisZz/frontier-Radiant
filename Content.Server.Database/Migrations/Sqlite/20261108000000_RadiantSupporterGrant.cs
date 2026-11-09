using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

[DbContext(typeof(SqliteServerDbContext))]
[Migration("20261108000000_RadiantSupporterGrant")]
public sealed class RadiantSupporterGrant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateTable("radiant_supporter_grant", columns: table => new
        {
            user_id = table.Column<Guid>(type: "TEXT", nullable: false),
            expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: table => table.PrimaryKey("PK_radiant_supporter_grant", row => row.user_id));

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable("radiant_supporter_grant");
}
