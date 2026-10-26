using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RecipePlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientNutritionCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngredientNutrition",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NameKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FdcId = table.Column<int>(type: "integer", nullable: true),
                    FactsJson = table.Column<string>(type: "text", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientNutrition", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngredientNutrition_NameKey",
                table: "IngredientNutrition",
                column: "NameKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngredientNutrition");
        }
    }
}
