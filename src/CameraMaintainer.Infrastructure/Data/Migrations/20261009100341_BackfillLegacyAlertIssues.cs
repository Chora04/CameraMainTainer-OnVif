using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameraMaintainer.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLegacyAlertIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "alert_issues" (
                    "AlertId", "AlertType", "Category", "Severity", "Title", "Description",
                    "StartedAt", "RecoveredAt", "ResolvedByUserId", "ResolvedByUsername", "ResolvedByDisplayName")
                SELECT
                    alert."Id", alert."AlertType", alert."Category", alert."Severity", alert."Title", alert."Description",
                    alert."CreatedAt",
                    COALESCE(alert."RecoveryDetectedAt", CASE WHEN alert."AutoResolved" = 1 THEN alert."ResolvedAt" END),
                    NULL,
                    CASE WHEN alert."AutoResolved" = 1 THEN alert."ResolvedByUsername" END,
                    CASE WHEN alert."AutoResolved" = 1 THEN alert."ResolvedByDisplayName" END
                FROM "alerts" AS alert
                WHERE NOT EXISTS (
                    SELECT 1 FROM "alert_issues" AS issue WHERE issue."AlertId" = alert."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The inserted rows are now ordinary issue records and may own chat messages, so rollback preserves them.
        }
    }
}
