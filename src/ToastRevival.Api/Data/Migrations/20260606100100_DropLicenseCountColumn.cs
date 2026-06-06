using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLicenseCountColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DC-H2: LicenseCount was a dead column — device limits are now enforced
            // via BillingPlanRules and Stripe; this column was never read in production.
            migrationBuilder.DropColumn(
                name: "LicenseCount",
                table: "Tenants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LicenseCount",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
