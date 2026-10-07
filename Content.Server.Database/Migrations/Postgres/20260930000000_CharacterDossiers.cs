using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Postgres;

[DbContext(typeof(PostgresServerDbContext))]
[Migration("20260930000000_CharacterDossiers")]
public sealed class CharacterDossiers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("dossier_json", "profile", type: "text", nullable: false, defaultValue: "{}");
        migrationBuilder.AddColumn<string>("residence", "profile", type: "text", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("family_status", "profile", type: "text", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("children", "profile", type: "text", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("emergency_contact", "profile", type: "text", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>("distinguishing_features", "profile", type: "text", nullable: false, defaultValue: "");
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
