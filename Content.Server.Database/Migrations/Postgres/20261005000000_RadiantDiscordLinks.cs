using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Postgres;

[DbContext(typeof(PostgresServerDbContext))]
[Migration("20261005000000_RadiantDiscordLinks")]
public sealed class RadiantDiscordLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("radiant_discord_link", columns: table => new
        {
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            discord_user_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
        }, constraints: table => table.PrimaryKey("PK_radiant_discord_link", x => x.user_id));
        migrationBuilder.CreateIndex("IX_radiant_discord_link_discord_user_id", "radiant_discord_link",
            "discord_user_id", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable("radiant_discord_link");
}
