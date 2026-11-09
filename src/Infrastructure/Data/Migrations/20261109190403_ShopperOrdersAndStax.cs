using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShopperOrdersAndStax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShopperOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentState = table.Column<int>(type: "int", nullable: false),
                    PaymentMethodId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PaymentMethodType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    StaxTransactionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PaymentError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PaymentAttempts = table.Column<int>(type: "int", nullable: false),
                    PaymentKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RefundState = table.Column<int>(type: "int", nullable: false),
                    RefundRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundTransactionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RefundError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RefundedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopperOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaxCustomerLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StaxCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaxCustomerLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShopperOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogItemId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Units = table.Column<int>(type: "int", nullable: false),
                    ShopperOrderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopperOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopperOrderLines_ShopperOrders_ShopperOrderId",
                        column: x => x.ShopperOrderId,
                        principalTable: "ShopperOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopperOrderLines_ShopperOrderId",
                table: "ShopperOrderLines",
                column: "ShopperOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopperOrders_BuyerEmail",
                table: "ShopperOrders",
                column: "BuyerEmail");

            migrationBuilder.CreateIndex(
                name: "IX_StaxCustomerLinks_Email",
                table: "StaxCustomerLinks",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopperOrderLines");

            migrationBuilder.DropTable(
                name: "StaxCustomerLinks");

            migrationBuilder.DropTable(
                name: "ShopperOrders");

        }
    }
}
