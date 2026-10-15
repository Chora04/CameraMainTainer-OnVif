using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CurrentCameraHealthOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_health_checks_CameraId_CheckedAt",
                table: "health_checks");

            migrationBuilder.DropIndex(
                name: "IX_health_checks_CheckedAt",
                table: "health_checks");

            migrationBuilder.Sql(
                """
                DELETE FROM health_checks
                WHERE EXISTS (
                    SELECT 1
                    FROM health_checks AS newer
                    WHERE newer.CameraId = health_checks.CameraId
                      AND (
                          newer.CheckedAt > health_checks.CheckedAt
                          OR (
                              newer.CheckedAt = health_checks.CheckedAt
                              AND newer.Id > health_checks.Id
                          )
                      )
                );

                DELETE FROM alerts
                WHERE Severity = 'Info';

                UPDATE alerts
                SET AlertType = 'LensObstructed'
                WHERE Title LIKE 'Lens issue:%';

                DELETE FROM alerts
                WHERE EXISTS (
                    SELECT 1
                    FROM alerts AS newer
                    WHERE (
                            newer.CameraId = alerts.CameraId
                            OR (
                                newer.CameraId IS NULL
                                AND alerts.CameraId IS NULL
                            )
                          )
                      AND newer.AlertType = alerts.AlertType
                      AND newer.Title = alerts.Title
                      AND newer.Id > alerts.Id
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_health_checks_CameraId",
                table: "health_checks",
                column: "CameraId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_health_checks_CameraId",
                table: "health_checks");

            migrationBuilder.CreateIndex(
                name: "IX_health_checks_CameraId_CheckedAt",
                table: "health_checks",
                columns: new[] { "CameraId", "CheckedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_health_checks_CheckedAt",
                table: "health_checks",
                column: "CheckedAt");
        }
    }
}
