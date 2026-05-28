using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <summary>
    /// Adds admin-controllable panel translucency for the M12 desktop overlay.
    /// Range is enforced server-side at 10..100 in 5% increments — this column
    /// is just an int with a sane default (85) matching the pre-control hardcoded
    /// value the agent was using in 0.4.9..0.4.14.
    /// </summary>
    public partial class M12_OverlayOpacity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DesktopOverlayOpacityPercent",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 85);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesktopOverlayOpacityPercent",
                table: "Tenants");
        }
    }
}
