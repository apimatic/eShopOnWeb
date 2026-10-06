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
                    MerchantId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: false),
                    CatalogItemId = table.Column<int>(type: "int", nullable: false),
                    SquareItemId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: true),
                    SquareVariationId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PendingIdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PendingName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PendingAmount = table.Column<long>(type: "bigint", nullable: true),
                    PendingCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    PendingSince = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PhotoState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PhotoSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PhotoIdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PhotoClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PhotoImageId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: true),
                    PhotoImageUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquareCatalogLinks", x => new { x.MerchantId, x.CatalogItemId });
                });

            migrationBuilder.CreateTable(
                name: "SquareConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    MerchantId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquareConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SquareOAuthStates",
                columns: table => new
                {
                    StateHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquareOAuthStates", x => x.StateHash);
                });

            migrationBuilder.CreateTable(
                name: "SquareOrderLinks",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    MerchantId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: false),
                    LocationId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(192)", maxLength: 192, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SquareOrderId = table.Column<string>(type: "nvarchar(191)", maxLength: 191, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "SquareConnections");

            migrationBuilder.DropTable(
                name: "SquareOAuthStates");

            migrationBuilder.DropTable(
                name: "SquareOrderLinks");

        }
    }
}
