using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinixPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AuthorizedAmountCents",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CapturedAmountCents",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FinixAuthorizationId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinixAuthorizationState",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FinixFeeCents",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FinixTransferId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinixTransferState",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinixVoidState",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FulfilledAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FulfilmentStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotFulfilled");

            migrationBuilder.AddColumn<long>(
                name: "NetAmountCents",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentAttemptId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentCurrency",
                table: "Orders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentFailureMessage",
                table: "Orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "AwaitingPayment");

            migrationBuilder.AddColumn<long>(
                name: "RefundedAmountCents",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "SavedPaymentMethodId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinixCustomerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FinixIdentityId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinixCustomerProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FinixTransferId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FinixTransferState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FailureMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefunds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FinixIdentityId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FinixPaymentInstrumentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastFour = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ExpirationMonth = table.Column<int>(type: "int", nullable: true),
                    ExpirationYear = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinixCustomerProfiles_BuyerId",
                table: "FinixCustomerProfiles",
                column: "BuyerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_OrderId_IdempotencyKey",
                table: "OrderRefunds",
                columns: new[] { "OrderId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_BuyerId_FinixPaymentInstrumentId",
                table: "SavedPaymentMethods",
                columns: new[] { "BuyerId", "FinixPaymentInstrumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinixCustomerProfiles");

            migrationBuilder.DropTable(
                name: "OrderRefunds");

            migrationBuilder.DropTable(
                name: "SavedPaymentMethods");

            migrationBuilder.DropColumn(
                name: "AuthorizedAmountCents",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CapturedAmountCents",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinixAuthorizationId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinixAuthorizationState",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinixFeeCents",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinixTransferId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinixTransferState",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinixVoidState",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfilledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfilmentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "NetAmountCents",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentAttemptId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentCurrency",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentFailureMessage",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RefundedAmountCents",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SavedPaymentMethodId",
                table: "Orders");

            }
    }
}
