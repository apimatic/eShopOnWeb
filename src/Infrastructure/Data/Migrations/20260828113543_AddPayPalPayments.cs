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
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthorizationCreatedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthorizationExpiresAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AuthorizedAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CapturedAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FulfilledAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetProceeds",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderTotal",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PayPalAuthorizeRequestId",
                table: "Orders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalCaptureRequestId",
                table: "Orders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalCreateRequestId",
                table: "Orders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalReauthorizeRequestId",
                table: "Orders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PayPalFee",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalOrderId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalOrderStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalVoidRequestId",
                table: "Orders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentCorrelationId",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentSource",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatus",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaymentUpdatedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReauthorizationCount",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayPalRefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PayPalStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Orders_OrderId",
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
                    OwnerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayPalTokenId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayPalCustomerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MerchantCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LastDigits = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Expiry = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    CardholderName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_AuthorizationId",
                table: "Orders",
                column: "AuthorizationId",
                unique: true,
                filter: "[AuthorizationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CaptureId",
                table: "Orders",
                column: "CaptureId",
                unique: true,
                filter: "[CaptureId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PayPalOrderId",
                table: "Orders",
                column: "PayPalOrderId",
                unique: true,
                filter: "[PayPalOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_OrderId_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "OrderId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_OwnerId_IsActive",
                table: "SavedPaymentMethods",
                columns: new[] { "OwnerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_PayPalTokenId",
                table: "SavedPaymentMethods",
                column: "PayPalTokenId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "SavedPaymentMethods");

            migrationBuilder.DropIndex(
                name: "IX_Orders_AuthorizationId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CaptureId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PayPalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizationCreatedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizationExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizationId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizationStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizedAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CaptureId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CaptureStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CapturedAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfilledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "NetProceeds",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderTotal",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalAuthorizeRequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalCaptureRequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalCreateRequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalReauthorizeRequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalOrderStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalVoidRequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentCorrelationId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentSource",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentUpdatedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReauthorizationCount",
                table: "Orders");
        }
    }
}
