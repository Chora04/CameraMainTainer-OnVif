using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CameraLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "cameras",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "cameras",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetAddress",
                table: "cameras",
                type: "TEXT",
                maxLength: 600,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "StreetAddress",
                table: "cameras");
        }
    }
}
