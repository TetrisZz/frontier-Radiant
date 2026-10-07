using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

[DbContext(typeof(SqliteServerDbContext))]
[Migration("20260914000000_ProfessionalSkills")]
public sealed class ProfessionalSkills : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>("skill_levels", "profile", type: "TEXT", nullable: false, defaultValue: "[]");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn("skill_levels", "profile");
}
