using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportCRM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogFilterIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Browse",
                table: "AuditLogs",
                columns: new[] { "OccurredAt", "EntityName", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId",
                filter: "[UserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Browse",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs");
        }
    }
}
