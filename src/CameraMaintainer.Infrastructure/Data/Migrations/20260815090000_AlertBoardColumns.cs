using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations;

public partial class AlertBoardColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "WorkflowStage",
            table: "alerts",
            type: "TEXT",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_alerts_WorkflowStage",
            table: "alerts",
            column: "WorkflowStage");

        migrationBuilder.Sql(
            """
            UPDATE alerts
            SET WorkflowStage = CASE
                WHEN AssignedToUserId IS NULL THEN 'new'
                WHEN WorkReport IS NULL OR trim(WorkReport) = '' THEN 'working'
                ELSE 'ready'
            END
            WHERE WorkflowStage IS NULL OR trim(WorkflowStage) = '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_alerts_WorkflowStage",
            table: "alerts");

        migrationBuilder.DropColumn(
            name: "WorkflowStage",
            table: "alerts");
    }
}
