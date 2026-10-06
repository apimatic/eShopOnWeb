using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "OrderPaymentAttempts",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MerchantReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PspReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ResultCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RefusalReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    RefusalReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ProviderHttpStatus = table.Column<int>(type: "int", nullable: true),
                    ProviderResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPaymentAttempts", x => new { x.OrderId, x.AttemptNumber });
                    table.ForeignKey(
                        name: "FK_OrderPaymentAttempts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderRefunds",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    RefundId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MerchantReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PaymentPspReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PspReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ProviderHttpStatus = table.Column<int>(type: "int", nullable: true),
                    ProviderResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefunds", x => new { x.OrderId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_OrderRefunds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPaymentAttempts_PspReference",
                table: "OrderPaymentAttempts",
                column: "PspReference");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_RefundId",
                table: "OrderRefunds",
                column: "RefundId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderPaymentAttempts");

            migrationBuilder.DropTable(
                name: "OrderRefunds");

        }
    }
}
