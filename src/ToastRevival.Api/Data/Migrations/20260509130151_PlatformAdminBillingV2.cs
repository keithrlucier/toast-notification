using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAdminBillingV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPlatformAdmin",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "AspNetUsers"
                SET "IsPlatformAdmin" = TRUE,
                    "Role" = 2
                WHERE UPPER("Email") = 'KEITHRLUCIER@GMAIL.COM'
                   OR "NormalizedEmail" = 'KEITHRLUCIER@GMAIL.COM'
                   OR UPPER("Email") = 'KEITH@COLOSOLUTIONS.COM'
                   OR "NormalizedEmail" = 'KEITH@COLOSOLUTIONS.COM';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPlatformAdmin",
                table: "AspNetUsers");
        }
    }
}
