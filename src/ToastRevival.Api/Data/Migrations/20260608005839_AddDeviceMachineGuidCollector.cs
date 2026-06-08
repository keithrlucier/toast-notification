using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceMachineGuidCollector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DnsHostName",
                table: "Devices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineGuid",
                table: "Devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_MachineGuid",
                table: "Devices",
                columns: new[] { "TenantId", "MachineGuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_TenantId_MachineGuid",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DnsHostName",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "MachineGuid",
                table: "Devices");
        }
    }
}
