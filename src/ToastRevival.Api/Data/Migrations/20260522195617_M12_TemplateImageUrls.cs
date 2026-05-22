using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class M12_TemplateImageUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HeroImageUrl",
                table: "NotificationTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoImageUrl",
                table: "NotificationTemplates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeroImageUrl",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "LogoImageUrl",
                table: "NotificationTemplates");
        }
    }
}
