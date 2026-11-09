using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Stax.Migrations
{
    /// <inheritdoc />
    public partial class InitialStax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShopOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PayState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayAttempts = table.Column<int>(type: "int", nullable: false),
                    PayIdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PaymentMethodId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PaymentMethodType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StaxTransactionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PaymentMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RefundRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RefundTransactionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RefundNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopOrders", x => x.Id);
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
                name: "ShopOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShopOrderId = table.Column<int>(type: "int", nullable: false),
                    CatalogItemId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Units = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopOrderLines_ShopOrders_ShopOrderId",
                        column: x => x.ShopOrderId,
                        principalTable: "ShopOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopOrderLines_ShopOrderId",
                table: "ShopOrderLines",
                column: "ShopOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopOrders_BuyerEmail",
                table: "ShopOrders",
                column: "BuyerEmail");

            migrationBuilder.CreateIndex(
                name: "IX_StaxCustomerLinks_Email",
                table: "StaxCustomerLinks",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_StaxCustomerLinks_Email_StaxCustomerId",
                table: "StaxCustomerLinks",
                columns: new[] { "Email", "StaxCustomerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopOrderLines");

            migrationBuilder.DropTable(
                name: "StaxCustomerLinks");

            migrationBuilder.DropTable(
                name: "ShopOrders");
        }
    }
}
