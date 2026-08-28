using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FulfillmentStatus",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    OrderAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InvoiceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayPalOrderId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AuthorizationAttemptCount = table.Column<int>(type: "int", nullable: false),
                    FundingBrand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FundingLastDigits = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    SavedPaymentMethodId = table.Column<int>(type: "int", nullable: true),
                    CaptureId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CaptureStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CapturedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PayPalFee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayPalPaymentTokenId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PayPalCustomerId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastDigits = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Expiry = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAuthorizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderPaymentId = table.Column<int>(type: "int", nullable: false),
                    PayPalAuthorizationId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpirationTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAuthorizations_OrderPayments_OrderPaymentId",
                        column: x => x.OrderPaymentId,
                        principalTable: "OrderPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderPaymentId = table.Column<int>(type: "int", nullable: false),
                    PayPalRefundId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_OrderPayments_OrderPaymentId",
                        column: x => x.OrderPaymentId,
                        principalTable: "OrderPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_CaptureId",
                table: "OrderPayments",
                column: "CaptureId",
                unique: true,
                filter: "[CaptureId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_InvoiceId",
                table: "OrderPayments",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_OrderId",
                table: "OrderPayments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_PayPalOrderId",
                table: "OrderPayments",
                column: "PayPalOrderId",
                unique: true,
                filter: "[PayPalOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAuthorizations_OrderPaymentId",
                table: "PaymentAuthorizations",
                column: "OrderPaymentId",
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAuthorizations_PayPalAuthorizationId",
                table: "PaymentAuthorizations",
                column: "PayPalAuthorizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_OrderPaymentId_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "OrderPaymentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_PayPalRefundId",
                table: "PaymentRefunds",
                column: "PayPalRefundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_BuyerId_IsActive",
                table: "SavedPaymentMethods",
                columns: new[] { "BuyerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_PayPalPaymentTokenId",
                table: "SavedPaymentMethods",
                column: "PayPalPaymentTokenId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentAuthorizations");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "SavedPaymentMethods");

            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "Orders");

        }
    }
}
