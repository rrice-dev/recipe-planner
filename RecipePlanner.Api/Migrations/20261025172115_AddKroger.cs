using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipePlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddKroger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KrogerConnections",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "text", nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "text", nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LocationId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LocationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KrogerConnections", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_KrogerConnections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KrogerProductChoices",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Upc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KrogerProductChoices", x => new { x.UserId, x.ItemKey });
                    table.ForeignKey(
                        name: "FK_KrogerProductChoices_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KrogerConnections");

            migrationBuilder.DropTable(
                name: "KrogerProductChoices");
        }
    }
}
