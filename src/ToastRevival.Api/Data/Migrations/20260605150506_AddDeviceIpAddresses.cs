using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceIpAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LanIpAddress",
                table: "Devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WanIpAddress",
                table: "Devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LanIpAddress",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "WanIpAddress",
                table: "Devices");
        }
    }
}
