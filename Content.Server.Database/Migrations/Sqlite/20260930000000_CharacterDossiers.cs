using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

[DbContext(typeof(SqliteServerDbContext))]
[Migration("20260930000000_CharacterDossiers")]
public sealed class CharacterDossiers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("dossier_json", "profile", type: "TEXT", nullable: false, defaultValue: "{}");
        migrationBuilder.AddColumn<string>("residence", "profile", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("family_status", "profile", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("children", "profile", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("emergency_contact", "profile", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("distinguishing_features", "profile", type: "TEXT", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("distinguishing_features", "profile");
        migrationBuilder.DropColumn("emergency_contact", "profile");
        migrationBuilder.DropColumn("children", "profile");
        migrationBuilder.DropColumn("family_status", "profile");
        migrationBuilder.DropColumn("residence", "profile");
        migrationBuilder.DropColumn("dossier_json", "profile");
    }
}
