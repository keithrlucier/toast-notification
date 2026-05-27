using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class M12DeviceAppearance : Migration
    {
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
