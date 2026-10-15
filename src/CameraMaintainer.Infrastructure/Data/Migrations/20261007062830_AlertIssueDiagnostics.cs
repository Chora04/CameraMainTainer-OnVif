using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlertIssueDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "alert_issues",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureDurationMs",
                table: "alert_issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureStage",
                table: "alert_issues",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureTimeoutMs",
                table: "alert_issues",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureCode",
                table: "alert_issues");

            migrationBuilder.DropColumn(
                name: "FailureDurationMs",
                table: "alert_issues");

            migrationBuilder.DropColumn(
                name: "FailureStage",
                table: "alert_issues");

            migrationBuilder.DropColumn(
                name: "FailureTimeoutMs",
                table: "alert_issues");
        }
    }
}
