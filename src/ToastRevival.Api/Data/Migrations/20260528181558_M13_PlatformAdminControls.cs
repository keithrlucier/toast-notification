using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class M13_PlatformAdminControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: DesktopOverlayOpacityPercent is intentionally NOT added here —
            // it was already shipped by M12_OverlayOpacity (2026-05-28). The EF
            // model snapshot lagged behind that migration, so `ef migrations add`
            // re-emitted the AddColumn. Removed by hand to keep prod migration
            // idempotent. The column ends up in the post-M13 snapshot anyway.
            migrationBuilder.AddColumn<string>(
                name: "ComplimentaryReason",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsComplimentary",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspendedReason",
                table: "Tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplimentaryReason",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IsComplimentary",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SuspendedReason",
                table: "Tenants");
        }
    }
}
