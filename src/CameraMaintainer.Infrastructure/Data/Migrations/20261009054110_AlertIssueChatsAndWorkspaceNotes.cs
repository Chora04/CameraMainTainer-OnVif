using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlertIssueChatsAndWorkspaceNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolvedByDisplayName",
                table: "alert_issues",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolvedByUserId",
                table: "alert_issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByUsername",
                table: "alert_issues",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE alert_issues
                SET ResolvedByUsername = (
                        SELECT ResolvedByUsername FROM alerts WHERE alerts.Id = alert_issues.AlertId),
                    ResolvedByDisplayName = (
                        SELECT ResolvedByDisplayName FROM alerts WHERE alerts.Id = alert_issues.AlertId)
                WHERE RecoveredAt IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM alerts
                      WHERE alerts.Id = alert_issues.AlertId
                        AND (alerts.ResolvedByUsername IS NOT NULL OR alerts.ResolvedByDisplayName IS NOT NULL));
                """);

            migrationBuilder.CreateTable(
                name: "alert_issue_messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlertIssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_issue_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_issue_messages_alert_issues_AlertIssueId",
                        column: x => x.AlertIssueId,
                        principalTable: "alert_issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_notes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Tone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletedByUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CompletedByDisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_notes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_issue_messages_AlertIssueId_CreatedAt",
                table: "alert_issue_messages",
                columns: new[] { "AlertIssueId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_notes_CompletedAt_ScheduledFor",
                table: "workspace_notes",
                columns: new[] { "CompletedAt", "ScheduledFor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_issue_messages");

            migrationBuilder.DropTable(
                name: "workspace_notes");

            migrationBuilder.DropColumn(
                name: "ResolvedByDisplayName",
                table: "alert_issues");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "alert_issues");

            migrationBuilder.DropColumn(
                name: "ResolvedByUsername",
                table: "alert_issues");
        }
    }
}
