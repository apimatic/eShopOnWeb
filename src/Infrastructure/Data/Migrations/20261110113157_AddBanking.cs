using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {





            migrationBuilder.CreateTable(
                name: "BankCheckouts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Shipping = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankCheckouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InstitutionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InstitutionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccountMask = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SyncCursor = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DisconnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CheckoutId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Shipping = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Charged = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Speed = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TransferId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ArrivesOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ShippedOn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankOrders", x => x.Id);
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
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Pending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_OwnerId",
                table: "BankConnections",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BankOrders_CheckoutId",
                table: "BankOrders",
                column: "CheckoutId");

            migrationBuilder.CreateIndex(
                name: "IX_BankOrders_OwnerId",
                table: "BankOrders",
                column: "OwnerId");

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
                name: "BankCheckouts");

            migrationBuilder.DropTable(
                name: "BankConnections");

            migrationBuilder.DropTable(
                name: "BankOrders");

            migrationBuilder.DropTable(
                name: "BankTransactions");





        }
    }
}
