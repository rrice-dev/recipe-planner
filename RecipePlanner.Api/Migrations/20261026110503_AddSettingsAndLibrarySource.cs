using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipePlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsAndLibrarySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recipes_OwnerId",
                table: "Recipes");

            migrationBuilder.AddColumn<string>(
                name: "LibrarySlug",
                table: "Recipes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultServings",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<string>(
                name: "Diet",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "AspNetUsers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_OwnerId_LibrarySlug",
                table: "Recipes",
                columns: new[] { "OwnerId", "LibrarySlug" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recipes_OwnerId_LibrarySlug",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "LibrarySlug",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "DefaultServings",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Diet",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_OwnerId",
                table: "Recipes",
                column: "OwnerId");
        }
    }
}
