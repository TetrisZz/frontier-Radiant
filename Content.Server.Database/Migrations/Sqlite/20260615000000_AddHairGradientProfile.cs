using Content.Server.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    [DbContext(typeof(SqliteServerDbContext))]
    [Migration("20260615000000_AddHairGradientProfile")]
    public partial class AddHairGradientProfile : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "hair_coloring_mode",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "hair_gradient_color",
                table: "profile",
                type: "TEXT",
                nullable: false,
                defaultValue: "#FFFFFF");

            migrationBuilder.AddColumn<int>(
                name: "hair_gradient_direction",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "facial_hair_coloring_mode",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "facial_hair_gradient_color",
                table: "profile",
                type: "TEXT",
                nullable: false,
                defaultValue: "#FFFFFF");

            migrationBuilder.AddColumn<int>(
                name: "facial_hair_gradient_direction",
                table: "profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hair_coloring_mode",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "hair_gradient_color",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "hair_gradient_direction",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "facial_hair_coloring_mode",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "facial_hair_gradient_color",
                table: "profile");

            migrationBuilder.DropColumn(
                name: "facial_hair_gradient_direction",
                table: "profile");
        }
    }
}
