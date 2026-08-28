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
                name: "Status",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Placed");

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    OrderAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreateRequestId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorizeRequestId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CaptureRequestId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VoidRequestId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReauthorizeRequestId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PayPalOrderId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PayPalOrderStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthorizationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthorizationStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthorizedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AuthorizationCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizationExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizationGeneration = table.Column<int>(type: "int", nullable: false),
                    CaptureId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CaptureStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CapturedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PayPalFee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MerchantNet = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CaptureCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastProviderError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreateRequestId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayPalTokenId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PayPalCustomerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastDigits = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Expiry = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CardType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerificationStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentRecordId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(108)", maxLength: 108, nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayPalRefundId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PayPalStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProviderCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Payments_PaymentRecordId",
                        column: x => x.PaymentRecordId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_PaymentRecordId_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "PaymentRecordId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_OwnerId_PayPalTokenId",
                table: "SavedPaymentMethods",
                columns: new[] { "OwnerId", "PayPalTokenId" },
                unique: true,
                filter: "[PayPalTokenId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "SavedPaymentMethods");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");

        }
    }
}
