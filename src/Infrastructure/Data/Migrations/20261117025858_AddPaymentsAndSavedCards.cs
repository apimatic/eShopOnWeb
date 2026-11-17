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
                name: "PaymentReference",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "AwaitingPayment");

            migrationBuilder.Sql(
                "UPDATE Orders SET PaymentReference = CONVERT(nvarchar(64), NEWID()) WHERE PaymentReference IS NULL OR PaymentReference = '';");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentReference",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FinixIdentityId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CardId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CardType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ExpirationMonth = table.Column<int>(type: "int", nullable: false),
                    ExpirationYear = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    FinixPaymentInstrumentId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FinixAuthorizationId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AuthorizationState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AuthorizationExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizationAttempt = table.Column<int>(type: "int", nullable: false),
                    FinixTransferId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CaptureState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CapturedAmountCents = table.Column<int>(type: "int", nullable: false),
                    FeeCents = table.Column<int>(type: "int", nullable: false),
                    NetCents = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    FinixRefundId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ParentTransferId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Refunds_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_BuyerId_CardId",
                table: "PaymentMethods",
                columns: new[] { "BuyerId", "CardId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_FinixAuthorizationId",
                table: "Payments",
                column: "FinixAuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_FinixTransferId",
                table: "Payments",
                column: "FinixTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_FinixRefundId",
                table: "Refunds",
                column: "FinixRefundId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PaymentId_IdempotencyKey",
                table: "Refunds",
                columns: new[] { "PaymentId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "Refunds");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");
        }
    }
}
