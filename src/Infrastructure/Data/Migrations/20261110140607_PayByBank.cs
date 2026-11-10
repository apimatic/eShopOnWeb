using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PayByBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaymentFee",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTransferId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingCost",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);





            migrationBuilder.CreateTable(
                name: "BankCheckouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShipStreet = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    ShipCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ShipState = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ShipCountry = table.Column<string>(type: "nvarchar(90)", maxLength: 90, nullable: true),
                    ShipZipCode = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    ShippingCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Speed = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ArrivesOn = table.Column<DateTime>(type: "date", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    ConfirmingSince = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false)
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
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    PlaidItemId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InstitutionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InstitutionName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AccountName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AccountMask = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TransactionsCursor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectionId = table.Column<int>(type: "int", nullable: false),
                    PlaidTransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Pending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_BuyerId_PlaidItemId",
                table: "BankConnections",
                columns: new[] { "BuyerId", "PlaidItemId" },
                unique: true);

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
                name: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "AmountPaid",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentTransferId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingCost",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");





        }
    }
}
