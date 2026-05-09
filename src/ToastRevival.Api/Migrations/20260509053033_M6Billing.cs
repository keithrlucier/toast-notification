using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Migrations
{
    /// <inheritdoc />
    public partial class M6Billing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PastDueAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_ScheduledAt",
                table: "Notifications",
                columns: new[] { "Status", "ScheduledAt" },
                filter: "\"ScheduledAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Status_ScheduledAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "PastDueAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "Tenants");
        }
    }
}
