using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CanViewCameras = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanRunChecks = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanManageCameras = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanManageUsers = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cameras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", nullable: true),
                    FirmwareVersion = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCheckAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    TemperatureC = table.Column<double>(type: "REAL", nullable: true),
                    UptimeSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    PtzSupported = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventsSupported = table.Column<bool>(type: "INTEGER", nullable: false),
                    CapabilitiesRaw = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    AutoBackup = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoRecovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    CheckIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DirectionStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, defaultValue: "Unknown"),
                    DirectionLastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastEdgeDensity = table.Column<double>(type: "REAL", nullable: true),
                    LastTextureScore = table.Column<double>(type: "REAL", nullable: true),
                    BaselineHistogramJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cameras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_log",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobType = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CameraIds = table.Column<string>(type: "TEXT", nullable: false),
                    ParamsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TotalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scan_records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScanType = table.Column<string>(type: "TEXT", nullable: false),
                    CamerasFound = table.Column<int>(type: "INTEGER", nullable: false),
                    CamerasAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<int>(type: "INTEGER", nullable: true),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AlertType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    AutoResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcknowledgedBy = table.Column<string>(type: "TEXT", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alerts_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "config_backups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<int>(type: "INTEGER", nullable: false),
                    BackupType = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigXml = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_backups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_config_backups_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordEnc = table.Column<string>(type: "TEXT", nullable: false),
                    AuthType = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credentials_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "firmware_records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<int>(type: "INTEGER", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    HardwareId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_firmware_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_firmware_records_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "health_checks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<int>(type: "INTEGER", nullable: false),
                    Reachable = table.Column<bool>(type: "INTEGER", nullable: false),
                    StatusCode = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseTimeMs = table.Column<int>(type: "INTEGER", nullable: false),
                    TemperatureC = table.Column<double>(type: "REAL", nullable: true),
                    UptimeSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    DetailJson = table.Column<string>(type: "TEXT", nullable: false),
                    JitterMs = table.Column<double>(type: "REAL", nullable: true),
                    PacketLossPercent = table.Column<double>(type: "REAL", nullable: true),
                    SnapshotSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    DirectionStatus = table.Column<string>(type: "TEXT", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_health_checks_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ptz_presets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileToken = table.Column<string>(type: "TEXT", nullable: false),
                    PresetToken = table.Column<string>(type: "TEXT", nullable: false),
                    PresetName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ptz_presets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ptz_presets_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_AutoResolved",
                table: "alerts",
                column: "AutoResolved");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_CameraId",
                table: "alerts",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_CreatedAt",
                table: "alerts",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_alerts_Severity",
                table: "alerts",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_app_users_Username",
                table: "app_users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cameras_IpAddress",
                table: "cameras",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_cameras_LastCheckAt",
                table: "cameras",
                column: "LastCheckAt");

            migrationBuilder.CreateIndex(
                name: "IX_cameras_SerialNumber",
                table: "cameras",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cameras_Status",
                table: "cameras",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_config_backups_CameraId_CreatedAt",
                table: "config_backups",
                columns: new[] { "CameraId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_credentials_CameraId",
                table: "credentials",
                column: "CameraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_firmware_records_CameraId",
                table: "firmware_records",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_firmware_records_CameraId_DetectedAt",
                table: "firmware_records",
                columns: new[] { "CameraId", "DetectedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_health_checks_CameraId_CheckedAt",
                table: "health_checks",
                columns: new[] { "CameraId", "CheckedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_health_checks_CheckedAt",
                table: "health_checks",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_log_CreatedAt",
                table: "maintenance_log",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_log_Status",
                table: "maintenance_log",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ptz_presets_CameraId",
                table: "ptz_presets",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_ptz_presets_CameraId_ProfileToken_PresetToken",
                table: "ptz_presets",
                columns: new[] { "CameraId", "ProfileToken", "PresetToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_CreatedAt",
                table: "scan_records",
                column: "CreatedAt",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropTable(
                name: "app_users");

            migrationBuilder.DropTable(
                name: "config_backups");

            migrationBuilder.DropTable(
                name: "credentials");

            migrationBuilder.DropTable(
                name: "firmware_records");

            migrationBuilder.DropTable(
                name: "health_checks");

            migrationBuilder.DropTable(
                name: "maintenance_log");

            migrationBuilder.DropTable(
                name: "ptz_presets");

            migrationBuilder.DropTable(
                name: "scan_records");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "cameras");
        }
    }
}
