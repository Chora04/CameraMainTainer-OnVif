using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CameraCasesAndStableMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveHealthyChecks",
                table: "cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MergedIntoAlertId",
                table: "alerts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alert_issues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlertId = table.Column<int>(type: "INTEGER", nullable: false),
                    AlertType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecoveredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_issues_alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_MergedIntoAlertId",
                table: "alerts",
                column: "MergedIntoAlertId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_issues_AlertId_AlertType",
                table: "alert_issues",
                columns: new[] { "AlertId", "AlertType" });

            migrationBuilder.AddForeignKey(
                name: "FK_alerts_alerts_MergedIntoAlertId",
                table: "alerts",
                column: "MergedIntoAlertId",
                principalTable: "alerts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("""
                INSERT INTO alert_issues (AlertId, AlertType, Category, Severity, Title, Description, StartedAt, RecoveredAt)
                SELECT Id, AlertType, Category, Severity, Title, Description, CreatedAt,
                    COALESCE(RecoveryDetectedAt, CASE WHEN AutoResolved = 1 THEN ResolvedAt END)
                FROM alerts;

                CREATE TEMP TABLE camera_case_merge AS
                SELECT Id AS SourceId,
                    FIRST_VALUE(Id) OVER (PARTITION BY CameraId ORDER BY CreatedAt, Id) AS TargetId
                FROM alerts WHERE CameraId IS NOT NULL AND AutoResolved = 0;
                DELETE FROM camera_case_merge WHERE SourceId = TargetId;

                INSERT INTO alert_audit_events
                    (AlertId, EventType, ActorUsername, ActorDisplayName, Summary, FromStage, ToStage, PreviousValuesJson, NewValuesJson, CreatedAt)
                SELECT m.TargetId, 'case_merged', 'system', 'Система',
                    'Присоединена незакрытая карточка #' || a.Id || ': ' || a.Title,
                    a.WorkflowStage, (SELECT WorkflowStage FROM alerts WHERE Id = m.TargetId),
                    json_object('sourceAlertId', a.Id, 'assignedToUserId', a.AssignedToUserId,
                        'assignedToDisplayName', a.AssignedToDisplayName, 'workReport', a.WorkReport,
                        'serviceNotified', a.ServiceNotified, 'createdAt', a.CreatedAt),
                    NULL, strftime('%Y-%m-%d %H:%M:%f', 'now')
                FROM camera_case_merge m JOIN alerts a ON a.Id = m.SourceId;

                UPDATE alert_audit_events SET
                    Summary = 'Карточка #' || AlertId || ': ' || Summary,
                    AlertId = (SELECT TargetId FROM camera_case_merge WHERE SourceId = AlertId)
                WHERE AlertId IN (SELECT SourceId FROM camera_case_merge);
                UPDATE alert_handoffs SET AlertId = (SELECT TargetId FROM camera_case_merge WHERE SourceId = AlertId)
                WHERE AlertId IN (SELECT SourceId FROM camera_case_merge);
                UPDATE user_notifications SET
                    Url = '/Alerts?view=active&alert=' || (SELECT TargetId FROM camera_case_merge WHERE SourceId = AlertId),
                    AlertId = (SELECT TargetId FROM camera_case_merge WHERE SourceId = AlertId)
                WHERE AlertId IN (SELECT SourceId FROM camera_case_merge);
                UPDATE alert_issues SET AlertId = (SELECT TargetId FROM camera_case_merge WHERE SourceId = AlertId)
                WHERE AlertId IN (SELECT SourceId FROM camera_case_merge);
                UPDATE alerts SET MergedIntoAlertId = (SELECT TargetId FROM camera_case_merge WHERE SourceId = Id)
                WHERE Id IN (SELECT SourceId FROM camera_case_merge);

                UPDATE alerts SET
                    RecoveryDetectedAt = CASE WHEN EXISTS (SELECT 1 FROM alert_issues WHERE AlertId = alerts.Id AND RecoveredAt IS NULL)
                        THEN NULL ELSE (SELECT MAX(RecoveredAt) FROM alert_issues WHERE AlertId = alerts.Id) END,
                    WorkflowStage = CASE
                        WHEN NOT EXISTS (SELECT 1 FROM alert_issues WHERE AlertId = alerts.Id AND RecoveredAt IS NULL) THEN 'ready'
                        WHEN WorkflowStage = 'ready' THEN CASE WHEN AssignedToUserId IS NULL THEN 'new' ELSE 'working' END
                        ELSE WorkflowStage END,
                    Category = CASE WHEN EXISTS (SELECT 1 FROM alert_issues WHERE AlertId = alerts.Id AND Category = 'Physical' AND RecoveredAt IS NULL)
                        THEN 'Physical'
                        WHEN EXISTS (SELECT 1 FROM alert_issues WHERE AlertId = alerts.Id AND RecoveredAt IS NULL) THEN 'Technical'
                        WHEN EXISTS (SELECT 1 FROM alert_issues WHERE AlertId = alerts.Id AND Category = 'Physical') THEN 'Physical'
                        ELSE 'Technical' END
                WHERE Id IN (SELECT TargetId FROM camera_case_merge);

                UPDATE alerts SET (AlertType, Title, Description, Severity) = (
                    SELECT AlertType, Title, Description, Severity FROM alert_issues WHERE AlertId = alerts.Id
                    ORDER BY CASE WHEN RecoveredAt IS NULL THEN 0 ELSE 1 END,
                        CASE Severity WHEN 'Critical' THEN 2 WHEN 'Warning' THEN 1 ELSE 0 END DESC, StartedAt, Id
                    LIMIT 1)
                WHERE Id IN (SELECT TargetId FROM camera_case_merge);
                DROP TABLE camera_case_merge;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_alerts_alerts_MergedIntoAlertId",
                table: "alerts");

            migrationBuilder.DropTable(
                name: "alert_issues");

            migrationBuilder.DropIndex(
                name: "IX_alerts_MergedIntoAlertId",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ConsecutiveHealthyChecks",
                table: "cameras");

            migrationBuilder.DropColumn(
                name: "MergedIntoAlertId",
                table: "alerts");
        }
    }
}
