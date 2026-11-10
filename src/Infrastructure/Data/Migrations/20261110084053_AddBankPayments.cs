using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBankPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankCheckouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BankConnectionId = table.Column<int>(type: "int", nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShipToJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ItemsTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShippingCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InstitutionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InstitutionName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PaymentAccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PaymentAccountName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PaymentAccountMask = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TransactionsCursor = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Speed = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ArrivesOn = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ShippedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankPayments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankConnectionId = table.Column<int>(type: "int", nullable: false),
                    PlaidTransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsoCurrencyCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Pending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransactions_BankConnections_BankConnectionId",
                        column: x => x.BankConnectionId,
                        principalTable: "BankConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankPaymentTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankPaymentId = table.Column<int>(type: "int", nullable: false),
                    PlaidTransferId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankPaymentTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankPaymentTransfers_BankPayments_BankPaymentId",
                        column: x => x.BankPaymentId,
                        principalTable: "BankPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankCheckouts_UserName",
                table: "BankCheckouts",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_PublicId",
                table: "BankConnections",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_UserName",
                table: "BankConnections",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_UserName_ItemId",
                table: "BankConnections",
                columns: new[] { "UserName", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankPayments_CheckoutId",
                table: "BankPayments",
                column: "CheckoutId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankPayments_OrderId",
                table: "BankPayments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentTransfers_BankPaymentId",
                table: "BankPaymentTransfers",
                column: "BankPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankConnectionId_PlaidTransactionId",
                table: "BankTransactions",
                columns: new[] { "BankConnectionId", "PlaidTransactionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankCheckouts");

            migrationBuilder.DropTable(
                name: "BankPaymentTransfers");

            migrationBuilder.DropTable(
                name: "BankTransactions");

            migrationBuilder.DropTable(
                name: "BankPayments");

            migrationBuilder.DropTable(
                name: "BankConnections");





        }
    }
}
