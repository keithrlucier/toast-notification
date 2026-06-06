using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReviewSchemaCleanup_DropDeadColumns_TenantIdIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_Devices_TenantId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LicenseCount",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionTier",
                table: "Tenants");

            // Prod already carries IX_Notifications_TenantId (created out-of-band before
            // the migration history was reconciled); fresh databases do not. IF NOT EXISTS
            // makes this idempotent: a no-op on prod, and creates it on a clean build so
            // the model/DB stay consistent in every environment.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Notifications_TenantId\" ON \"Notifications\" (\"TenantId\");");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_CreatedAt",
                table: "Notifications",
                columns: new[] { "TenantId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_TenantId",
                table: "NotificationDeliveries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_Status",
                table: "Devices",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_Notifications_TenantId\";");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_TenantId_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_TenantId",
                table: "NotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Devices_TenantId_Status",
                table: "Devices");

            migrationBuilder.AddColumn<int>(
                name: "LicenseCount",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionTier",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TenantApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantApiKeys_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId",
                table: "Devices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantApiKeys_KeyHash",
                table: "TenantApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantApiKeys_TenantId",
                table: "TenantApiKeys",
                column: "TenantId");
        }
    }
}
