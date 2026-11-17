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
            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    FinixOperationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AmountAuthorizedCents = table.Column<long>(type: "bigint", nullable: false),
                    AmountCapturedCents = table.Column<long>(type: "bigint", nullable: false),
                    AmountRefundedCents = table.Column<long>(type: "bigint", nullable: false),
                    FeeCents = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    FinixAuthorizationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AuthorizationState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AuthorizationVoided = table.Column<bool>(type: "bit", nullable: false),
                    AuthorizationExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CaptureTransferId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CaptureState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FinixPaymentInstrumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FinixIdentityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AuthorizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CanceledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FinixPaymentInstrumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FinixIdentityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ExpirationMonth = table.Column<int>(type: "int", nullable: true),
                    ExpirationYear = table.Column<int>(type: "int", nullable: true),
                    CardType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    OrderPaymentId = table.Column<int>(type: "int", nullable: false),
                    FinixRefundId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
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
                name: "IX_OrderPayments_OrderId",
                table: "OrderPayments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_OrderPaymentId_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "OrderPaymentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_BuyerId",
                table: "SavedPaymentMethods",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_FinixPaymentInstrumentId",
                table: "SavedPaymentMethods",
                column: "FinixPaymentInstrumentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "SavedPaymentMethods");

            migrationBuilder.DropTable(
                name: "OrderPayments");
        }
    }
}
