using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Migrations
{
    /// <summary>
    /// Adds AppUser.LastTotpStep so MfaService.Verify can reject TOTP code
    /// replay within the ±1 step verification window.
    /// </summary>
    public partial class M3MfaTotpReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastTotpStep",
                table: "AspNetUsers",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTotpStep",
                table: "AspNetUsers");
        }
    }
}
