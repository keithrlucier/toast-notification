using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToastRevival.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSigningKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SigningKey",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Backfill any pre-existing tenant rows with a unique random signing key.
            // Empty signing keys would let an attacker who knew the (empty) key forge
            // notification HMACs against that tenant's devices. Postgres gen_random_bytes()
            // requires pgcrypto; we use gen_random_uuid() which is built-in and produces
            // a 128-bit UUID per row, then encode hex (32 bytes) for a sufficiently
            // strong key. New tenants overwrite this via AuthController.Register.
            migrationBuilder.Sql(@"
                UPDATE ""Tenants""
                SET ""SigningKey"" = encode(gen_random_uuid()::text::bytea || gen_random_uuid()::text::bytea, 'base64')
                WHERE ""SigningKey"" = '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SigningKey",
                table: "Tenants");
        }
    }
}
