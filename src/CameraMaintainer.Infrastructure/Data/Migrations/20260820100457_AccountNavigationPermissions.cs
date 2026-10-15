using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountNavigationPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanViewAlertHistory",
                table: "app_users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewAlerts",
                table: "app_users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewDashboard",
                table: "app_users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewMaintenance",
                table: "app_users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanViewAlertHistory",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "CanViewAlerts",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "CanViewDashboard",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "CanViewMaintenance",
                table: "app_users");
        }
    }
}
