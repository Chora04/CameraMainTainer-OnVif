using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CameraAlertSerialNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alerts_CameraId",
                table: "alerts");

            migrationBuilder.AddColumn<int>(
                name: "NextAlertSequence",
                table: "cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CameraSequenceNumber",
                table: "alerts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE alerts
                SET CameraSequenceNumber = (
                    SELECT COUNT(*)
                    FROM alerts AS preceding
                    WHERE preceding.CameraId = alerts.CameraId
                      AND (
                          preceding.CreatedAt < alerts.CreatedAt
                          OR (preceding.CreatedAt = alerts.CreatedAt AND preceding.Id <= alerts.Id)
                      )
                )
                WHERE CameraId IS NOT NULL;

                UPDATE cameras
                SET NextAlertSequence = COALESCE((
                    SELECT MAX(alerts.CameraSequenceNumber) + 1
                    FROM alerts
                    WHERE alerts.CameraId = cameras.Id
                ), 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_alerts_CameraId_CameraSequenceNumber",
                table: "alerts",
                columns: new[] { "CameraId", "CameraSequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alerts_CameraId_CameraSequenceNumber",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "NextAlertSequence",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "CameraSequenceNumber",
                table: "alerts");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_CameraId",
                table: "alerts",
                column: "CameraId");
        }
    }
}
