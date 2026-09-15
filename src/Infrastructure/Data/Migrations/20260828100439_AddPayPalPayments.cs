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
            migrationBuilder.AddColumn<string>(
                name: "FulfilmentStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unfulfilled");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "AwaitingPayment");

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    InvoiceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreateOrderRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AuthorizeRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReauthorizeRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CaptureRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VoidRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayPalOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PayPalOrderStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AuthorizationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AuthorizationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AuthorizedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    OriginalAuthorizationTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizationTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizationExpirationTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CaptureId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CaptureStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CapturedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PayPalFee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CaptureTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreviousAuthorizationIds = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
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
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayPalVaultId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Expiry = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    CardholderName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderPaymentId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayPalRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayPalRefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
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
                name: "IX_OrderPayments_AuthorizationId",
                table: "OrderPayments",
                column: "AuthorizationId",
                unique: true,
                filter: "[AuthorizationId] IS NOT NULL");

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
                name: "IX_PaymentMethods_BuyerId",
                table: "PaymentMethods",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_PayPalVaultId",
                table: "PaymentMethods",
                column: "PayPalVaultId",
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
                unique: true,
                filter: "[PayPalRefundId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "FulfilmentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");

        }
    }
}
