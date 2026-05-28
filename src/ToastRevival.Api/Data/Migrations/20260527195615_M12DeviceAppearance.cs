using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class M12DeviceAppearance : Migration
    {
        // REVIEW-2026-05-28 Api-L1 REJECTED-by-design: text columns are unbounded but every
        // write path is admin-gated and enforces controller-side caps (CustomText<=80,
        // JoinFields whitelists ~70 bytes of canonical keys, LockScreenImageUrl is
        // server-constrained to /assets/lockscreen/). Editing a shipped migration to add
        // HasMaxLength is prohibited (Anthony's standing rule); a schema cap will ride on
        // the next net-new migration that touches these columns if one lands.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DesktopOverlayCustomText",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DesktopOverlayEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DesktopOverlayFields",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DesktopOverlayPosition",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LockScreenEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LockScreenImageUrl",
                table: "Tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesktopOverlayCustomText",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DesktopOverlayEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DesktopOverlayFields",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "DesktopOverlayPosition",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LockScreenEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LockScreenImageUrl",
                table: "Tenants");
        }
    }
}
