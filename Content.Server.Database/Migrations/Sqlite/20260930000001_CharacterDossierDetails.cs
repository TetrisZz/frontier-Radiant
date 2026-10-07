using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

[DbContext(typeof(SqliteServerDbContext))]
[Migration("20260930000001_CharacterDossierDetails")]
public sealed class CharacterDossierDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[] { "birthplace", "occupation", "education", "allergies", "medical_history", "blood_group" })
            migrationBuilder.AddColumn<string>(column, "profile", type: "TEXT", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[] { "birthplace", "occupation", "education", "allergies", "medical_history", "blood_group" })
            migrationBuilder.DropColumn(column, "profile");
    }
}
