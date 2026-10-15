using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlertAuditHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_audit_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlertId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActorUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorDisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    FromStage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ToStage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PreviousValuesJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    NewValuesJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_audit_events_alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_audit_events_ActorUserId",
                table: "alert_audit_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_audit_events_AlertId_CreatedAt",
                table: "alert_audit_events",
                columns: new[] { "AlertId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.Sql(
                """
                INSERT INTO alert_audit_events (
                    AlertId, EventType, ActorUserId, ActorUsername,
                    ActorDisplayName, Summary, FromStage, ToStage,
                    PreviousValuesJson, NewValuesJson, CreatedAt
                )
                SELECT
                    Id, 'legacy_imported', NULL, 'system', 'Система',
                    'Карточка перенесена из прежней истории; промежуточные изменения до этой даты недоступны',
                    NULL,
                    CASE WHEN AutoResolved = 1 THEN 'closed' ELSE WorkflowStage END,
                    NULL, NULL, COALESCE(UpdatedAt, CreatedAt)
                FROM alerts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_audit_events");
        }
    }
}
