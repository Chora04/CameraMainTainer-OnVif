using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ErrorWorkManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AcknowledgedBy",
                table: "alerts",
                newName: "AssignedToUsername");

            migrationBuilder.RenameColumn(
                name: "AcknowledgedAt",
                table: "alerts",
                newName: "AssignedAt");

            migrationBuilder.AddColumn<string>(
                name: "AssignedByUsername",
                table: "alerts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedToDisplayName",
                table: "alerts",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedToUserId",
                table: "alerts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "alerts",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Technical");

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByDisplayName",
                table: "alerts",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByUsername",
                table: "alerts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "alerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceNotified",
                table: "alerts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ServiceNotifiedByUsername",
                table: "alerts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServiceNotifiedAt",
                table: "alerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "alerts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "WorkReport",
                table: "alerts",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE alerts
                SET Category = CASE
                        WHEN AlertType IN ('LensObstructed', 'TemperatureHigh') THEN 'Physical'
                        ELSE 'Technical'
                    END,
                    AssignedToDisplayName = AssignedToUsername,
                    UpdatedAt = CreatedAt,
                    ResolvedAt = CASE WHEN AutoResolved = 1 THEN CreatedAt ELSE NULL END,
                    ResolvedByUsername = CASE WHEN AutoResolved = 1 THEN 'system' ELSE NULL END,
                    ResolvedByDisplayName = CASE WHEN AutoResolved = 1 THEN 'Система' ELSE NULL END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedByUsername",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "AssignedToDisplayName",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ResolvedByDisplayName",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ResolvedByUsername",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ServiceNotified",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ServiceNotifiedByUsername",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "ServiceNotifiedAt",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "WorkReport",
                table: "alerts");

            migrationBuilder.RenameColumn(
                name: "AssignedToUsername",
                table: "alerts",
                newName: "AcknowledgedBy");

            migrationBuilder.RenameColumn(
                name: "AssignedAt",
                table: "alerts",
                newName: "AcknowledgedAt");
        }
    }
}
