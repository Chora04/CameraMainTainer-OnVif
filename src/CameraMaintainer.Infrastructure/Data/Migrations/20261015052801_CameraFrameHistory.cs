using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CameraFrameHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "camera_frame_records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsAnomalous = table.Column<bool>(type: "INTEGER", nullable: false),
                    TriggerCodes = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DirectionStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ObstructionType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MeanBrightness = table.Column<double>(type: "REAL", nullable: false),
                    BrightnessStdDev = table.Column<double>(type: "REAL", nullable: false),
                    Entropy = table.Column<double>(type: "REAL", nullable: false),
                    EdgeDensity = table.Column<double>(type: "REAL", nullable: false),
                    TextureUniformityScore = table.Column<double>(type: "REAL", nullable: false),
                    DirectionConfidence = table.Column<double>(type: "REAL", nullable: false),
                    ImageRelativePath = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    ImageContentType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ImageSizeBytes = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_camera_frame_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_camera_frame_records_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_camera_frame_records_CameraId_CapturedAt",
                table: "camera_frame_records",
                columns: new[] { "CameraId", "CapturedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "camera_frame_records");
        }
    }
}
