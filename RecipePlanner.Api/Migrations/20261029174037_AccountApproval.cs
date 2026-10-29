using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipePlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccountApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsApproved",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Everyone who already has an account was let in before approvals existed, so they stay in.
            // Only accounts created from now on wait for an admin.
            migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"IsApproved\" = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsApproved",
                table: "AspNetUsers");
        }
    }
}
