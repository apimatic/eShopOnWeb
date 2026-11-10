using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayByBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccessToken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InstitutionId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InstitutionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentAccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HasBankAccounts = table.Column<bool>(type: "bit", nullable: false),
                    HasInvestmentAccounts = table.Column<bool>(type: "bit", nullable: false),
                    TransactionsCursor = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    PlaidAccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ItemsTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShippingCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Street = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(90)", maxLength: 90, nullable: false),
                    ZipCode = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Speed = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ChargedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AuthorizationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TransferId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ArrivesOn = table.Column<DateTime>(type: "date", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    ShippedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    PlaidTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Pending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_UserName",
                table: "BankConnections",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "IX_BankPayments_CheckoutId",
                table: "BankPayments",
                column: "CheckoutId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankPayments_OrderId",
                table: "BankPayments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_ConnectionId_PlaidTransactionId",
                table: "BankTransactions",
                columns: new[] { "ConnectionId", "PlaidTransactionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankConnections");

            migrationBuilder.DropTable(
                name: "BankPayments");

            migrationBuilder.DropTable(
                name: "BankTransactions");

        }
    }
}
