using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class M15_MfaEnforcement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireMfa",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MfaPendingSecret",
                table: "AspNetUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireMfa",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MfaPendingSecret",
                table: "AspNetUsers");
        }
    }
}
