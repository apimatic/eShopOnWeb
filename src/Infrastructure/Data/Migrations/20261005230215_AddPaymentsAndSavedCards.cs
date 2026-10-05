using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsAndSavedCards : Migration
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
                defaultValue: "AwaitingPayment");





            migrationBuilder.CreateTable(
                name: "PaymentClaims",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Owner = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentClaims", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CardId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Expiry = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    ProviderCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SaveRequestKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderDeletionPending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    InvoiceId = table.Column<string>(type: "nvarchar(127)", maxLength: 127, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaymentMethodId = table.Column<int>(type: "int", nullable: true),
                    CardBrand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CardLastDigits = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    AuthorizationRequestId = table.Column<string>(type: "nvarchar(108)", maxLength: 108, nullable: true),
                    ProviderOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AuthorizationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OriginalAuthorizationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AuthorizationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AuthorizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizationExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReauthorizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CaptureRequestId = table.Column<string>(type: "nvarchar(108)", maxLength: 108, nullable: true),
                    CaptureId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CaptureStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CapturedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    PayPalFee = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidRequestId = table.Column<string>(type: "nvarchar(108)", maxLength: 108, nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderRequestId = table.Column<string>(type: "nvarchar(108)", maxLength: 108, nullable: false),
                    ProviderRefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_BuyerId",
                table: "PaymentMethods",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_BuyerId_SaveRequestKey",
                table: "PaymentMethods",
                columns: new[] { "BuyerId", "SaveRequestKey" },
                unique: true,
                filter: "[SaveRequestKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_PaymentId_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "PaymentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentClaims");

            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");





        }
    }
}
