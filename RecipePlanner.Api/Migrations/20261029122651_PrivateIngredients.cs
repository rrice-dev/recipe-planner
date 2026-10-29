using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipePlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class PrivateIngredients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ingredients_Name",
                table: "Ingredients");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Ingredients",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_OwnerId_Name",
                table: "Ingredients",
                columns: new[] { "OwnerId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "FK_Ingredients_AspNetUsers_OwnerId",
                table: "Ingredients",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ingredients_AspNetUsers_OwnerId",
                table: "Ingredients");

            migrationBuilder.DropIndex(
                name: "IX_Ingredients_OwnerId_Name",
                table: "Ingredients");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Ingredients");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_Name",
                table: "Ingredients",
                column: "Name",
                unique: true);
        }
    }
}
