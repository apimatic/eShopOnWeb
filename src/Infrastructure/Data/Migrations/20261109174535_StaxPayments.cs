using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaxPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {





            migrationBuilder.CreateTable(
                name: "InvoiceSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaxScheduleId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StaxCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FirstInvoiceDate = table.Column<DateTime>(type: "date", nullable: false),
                    DayOfMonth = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StaxPaymentMethodId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StaxTransactionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RefundRequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RefundRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RefundAttempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentCustomers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StaxCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavedPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StaxPaymentMethodId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Last4 = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    Nickname = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sellers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaxMerchantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sellers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSchedules_StaxScheduleId",
                table: "InvoiceSchedules",
                column: "StaxScheduleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_BuyerId",
                table: "OrderPayments",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_OrderId",
                table: "OrderPayments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCustomers_BuyerId",
                table: "PaymentCustomers",
                column: "BuyerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_BuyerId",
                table: "SavedPaymentMethods",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_StaxPaymentMethodId",
                table: "SavedPaymentMethods",
                column: "StaxPaymentMethodId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceSchedules");

            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropTable(
                name: "PaymentCustomers");

            migrationBuilder.DropTable(
                name: "SavedPaymentMethods");

            migrationBuilder.DropTable(
                name: "Sellers");




        }
    }
}
