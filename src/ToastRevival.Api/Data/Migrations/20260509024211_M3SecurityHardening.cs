using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class M3SecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_DeviceId",
                table: "NotificationDeliveries");

            migrationBuilder.AddColumn<string>(
                name: "EnrollmentKey",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantBlocklistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Term = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBlocklistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantBlocklistEntries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_DeviceId_Status_CreatedAt",
                table: "NotificationDeliveries",
                columns: new[] { "DeviceId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantBlocklistEntries_TenantId_Term",
                table: "TenantBlocklistEntries",
                columns: new[] { "TenantId", "Term" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantBlocklistEntries");

            migrationBuilder.DropIndex(
                name: "IX_NotificationDeliveries_DeviceId_Status_CreatedAt",
                table: "NotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "EnrollmentKey",
                table: "Tenants");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_DeviceId",
                table: "NotificationDeliveries",
                column: "DeviceId");
        }
    }
}
