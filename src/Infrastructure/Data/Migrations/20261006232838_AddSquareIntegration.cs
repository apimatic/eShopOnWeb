using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSquareIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SquareCatalogLinks",
                columns: table => new
                {
                    MerchantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CatalogItemId = table.Column<int>(type: "int", nullable: false),
                    SquareItemId = table.Column<string>(type: "nvarchar(192)", maxLength: 192, nullable: false),
                    SquareVariationId = table.Column<string>(type: "nvarchar(192)", maxLength: 192, nullable: false),
                    SquareImageId = table.Column<string>(type: "nvarchar(192)", maxLength: 192, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquareCatalogLinks", x => new { x.MerchantId, x.CatalogItemId });
                });

            migrationBuilder.CreateTable(
                name: "SquareLeases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquareLeases", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "SquareMerchantConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    MerchantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquareMerchantConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SquareOAuthStates",
                columns: table => new
                {
                    State = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquareOAuthStates", x => x.State);
                });

            migrationBuilder.CreateTable(
                name: "SquareOrderLinks",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    MerchantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LocationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SquareOrderId = table.Column<string>(type: "nvarchar(192)", maxLength: 192, nullable: true),
                    LineCatalogObjectIds = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    HasGiftMessage = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquareOrderLinks", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_SquareOrderLinks_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SquareCatalogLinks");

            migrationBuilder.DropTable(
                name: "SquareLeases");

            migrationBuilder.DropTable(
                name: "SquareMerchantConnections");

            migrationBuilder.DropTable(
                name: "SquareOAuthStates");

            migrationBuilder.DropTable(
                name: "SquareOrderLinks");
        }
    }
}
