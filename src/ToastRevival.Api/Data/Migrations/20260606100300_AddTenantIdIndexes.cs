using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PERF-L1: Notifications — tenant list and time-range queries (dashboard, analytics).
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId",
                table: "Notifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_CreatedAt",
                table: "Notifications",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            // PERF-L1: NotificationDeliveries — tenant-scoped delivery queries.
            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_TenantId",
                table: "NotificationDeliveries",
                column: "TenantId");

            // PERF-L2: Devices — tenant+status composite for device list and active-device count.
            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_Status",
                table: "Devices",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_TenantId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_TenantId_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_TenantId",
                table: "NotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Devices_TenantId_Status",
                table: "Devices");
        }
    }
}
