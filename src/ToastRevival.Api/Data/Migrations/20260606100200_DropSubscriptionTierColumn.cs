using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropSubscriptionTierColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DC-M1: SubscriptionTier was a dead column — always "Standard" (single enum value).
            // BillingController still references the enum type (Routes agent to clean up);
            // the DB column is dropped here independently.
            migrationBuilder.DropColumn(
                name: "SubscriptionTier",
                table: "Tenants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore as int (EF Core maps enum to int by default in Postgres).
            migrationBuilder.AddColumn<int>(
                name: "SubscriptionTier",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
