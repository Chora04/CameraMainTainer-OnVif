using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlertHandoffWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_handoff_reasons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_handoff_reasons", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO alert_handoff_reasons
                    (Key, Name, IsActive, SortOrder, CreatedAt, UpdatedAt)
                VALUES
                    ('parts-replacement', 'Замена комплектующих', 1, 10, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('approval', 'Требуется согласование', 1, 20, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('site-access', 'Требуется доступ к объекту', 1, 30, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('waiting-parts', 'Ожидание комплектующих', 1, 40, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('clarification', 'Ожидание уточнения', 1, 50, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('specialist', 'Требуется другой специалист', 1, 60, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ('other', 'Другая причина', 1, 70, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                """);

            migrationBuilder.CreateTable(
                name: "alert_handoffs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlertId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentHandoffId = table.Column<long>(type: "INTEGER", nullable: true),
                    ReasonId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReasonName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FromUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FromDisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ToUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ToDisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    AcceptanceComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CompletionComment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_handoffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_handoffs_alert_handoff_reasons_ReasonId",
                        column: x => x.ReasonId,
                        principalTable: "alert_handoff_reasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_handoffs_alert_handoffs_ParentHandoffId",
                        column: x => x.ParentHandoffId,
                        principalTable: "alert_handoffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_handoffs_alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AlertId = table.Column<int>(type: "INTEGER", nullable: false),
                    HandoffId = table.Column<long>(type: "INTEGER", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Tone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReminderAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRemindedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReminderCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_notifications_alert_handoffs_HandoffId",
                        column: x => x.HandoffId,
                        principalTable: "alert_handoffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_user_notifications_alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_handoff_reasons_IsActive_SortOrder",
                table: "alert_handoff_reasons",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_handoff_reasons_Key",
                table: "alert_handoff_reasons",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_handoffs_AlertId_CreatedAt",
                table: "alert_handoffs",
                columns: new[] { "AlertId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_handoffs_ParentHandoffId",
                table: "alert_handoffs",
                column: "ParentHandoffId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_handoffs_ReasonId",
                table: "alert_handoffs",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_handoffs_ToUserId_State_DueAt",
                table: "alert_handoffs",
                columns: new[] { "ToUserId", "State", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_AlertId",
                table: "user_notifications",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_HandoffId",
                table: "user_notifications",
                column: "HandoffId");

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_UserId_ResolvedAt_CreatedAt",
                table: "user_notifications",
                columns: new[] { "UserId", "ResolvedAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_notifications");

            migrationBuilder.DropTable(
                name: "alert_handoffs");

            migrationBuilder.DropTable(
                name: "alert_handoff_reasons");
        }
    }
}
