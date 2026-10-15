using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CameraCheckQueueSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutomaticChecksEnabled",
                table: "cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckQueuePosition",
                table: "cameras",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE cameras SET CheckQueuePosition = " +
                "(SELECT COUNT(*) FROM cameras AS ordered WHERE ordered.Id <= cameras.Id)");

            migrationBuilder.CreateIndex(
                name: "IX_cameras_AutomaticChecksEnabled_CheckQueuePosition",
                table: "cameras",
                columns: new[] { "AutomaticChecksEnabled", "CheckQueuePosition" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cameras_AutomaticChecksEnabled_CheckQueuePosition",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "AutomaticChecksEnabled",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "CheckQueuePosition",
                table: "cameras");
        }
    }
}
