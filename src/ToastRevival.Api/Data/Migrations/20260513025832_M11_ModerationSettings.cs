using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <summary>
    /// M11 — per-tenant content moderation policy.
    ///
    /// Adds 9 columns to Tenants for admin-configurable moderation:
    ///   - Enable/disable, text/image scan toggles
    ///   - Review and Block severity thresholds (Azure Content Safety 0..6 scale)
    ///   - Require-admin-approval-for-all override
    ///   - Bring-your-own Azure Content Safety endpoint/key (per tenant)
    ///   - Custom blocked-content message shown to senders on 422
    ///
    /// Backfill defaults preserve the pre-M11 behavior:
    ///   ModerationEnabled = true   — service was always on (degraded to Pass if no key)
    ///   ModerationScanText = true
    ///   ModerationScanImages = true
    ///   ModerationReviewSeverity = 2
    ///   ModerationBlockSeverity = 5
    ///   ModerationRequireApprovalAll = false
    /// These match the hard-coded thresholds in ContentSafetyService prior to this migration.
    /// </summary>
    public partial class M11_ModerationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Severity thresholds — 2..4 = Review, 5..6 = Block (Azure Content Safety scale).
            // Backfill matches the pre-M11 hard-coded behavior so existing tenants see no
            // behavioral change on migration.
            migrationBuilder.AddColumn<int>(
                name: "ModerationReviewSeverity",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "ModerationBlockSeverity",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "ModerationEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ModerationScanText",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ModerationScanImages",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ModerationRequireApprovalAll",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ModerationCustomEndpoint",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationCustomKey",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationBlockedMessage",
                table: "Tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ModerationBlockSeverity",       table: "Tenants");
            migrationBuilder.DropColumn(name: "ModerationBlockedMessage",      table: "Tenants");
            migrationBuilder.DropColumn(name: "ModerationCustomEndpoint",      table: "Tenants");
            migrationBuilder.DropColumn(name: "ModerationCustomKey",           table: "Tenants");
            migrationBuilder.DropColumn(name: "ModerationEnabled",             table: "Tenants");
            migrationBuilder.DropColumn(name: "ModerationRequireApprovalAll",  table: "Tenants");
            migrationBuilder.DropColumn(name: "ModerationReviewSeverity",      table: "Tenants");
            migrationBuilder.DropColumn(name: "ModerationScanImages",          table: "Tenants");
            migrationBuilder.DropColumn(name: "ModerationScanText",            table: "Tenants");
        }
    }
}
