using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrivateWorkspaceNoteAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleToAll",
                table: "workspace_notes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecipientDisplayName",
                table: "workspace_notes",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecipientUserId",
                table: "workspace_notes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientUsername",
                table: "workspace_notes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_notes_CreatedByUserId_ScheduledFor",
                table: "workspace_notes",
                columns: new[] { "CreatedByUserId", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_notes_IsVisibleToAll_ScheduledFor",
                table: "workspace_notes",
                columns: new[] { "IsVisibleToAll", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_notes_RecipientUserId_ScheduledFor",
                table: "workspace_notes",
                columns: new[] { "RecipientUserId", "ScheduledFor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workspace_notes_CreatedByUserId_ScheduledFor",
                table: "workspace_notes");

            migrationBuilder.DropIndex(
                name: "IX_workspace_notes_IsVisibleToAll_ScheduledFor",
                table: "workspace_notes");

            migrationBuilder.DropIndex(
                name: "IX_workspace_notes_RecipientUserId_ScheduledFor",
                table: "workspace_notes");

            migrationBuilder.DropColumn(
                name: "IsVisibleToAll",
                table: "workspace_notes");

            migrationBuilder.DropColumn(
                name: "RecipientDisplayName",
                table: "workspace_notes");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "workspace_notes");

            migrationBuilder.DropColumn(
                name: "RecipientUsername",
                table: "workspace_notes");
        }
    }
}
