using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BankPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankCheckouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BankConnectionId = table.Column<int>(type: "int", nullable: false),
                    ShippingCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ShipStreet = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    ShipCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShipState = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ShipCountry = table.Column<string>(type: "nvarchar(90)", maxLength: 90, nullable: false),
                    ShipZipCode = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PlaidItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InstitutionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InstitutionName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PaymentAccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentAccountName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PaymentAccountMask = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    CheckoutId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Speed = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCharged = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ArrivesOn = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ShippedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankConnectionId = table.Column<int>(type: "int", nullable: false),
                    PlaidTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    Pending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BankCheckoutItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankCheckoutId = table.Column<int>(type: "int", nullable: false),
                    CatalogItemId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PictureUri = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Units = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankCheckoutItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankCheckoutItems_BankCheckouts_BankCheckoutId",
                        column: x => x.BankCheckoutId,
                        principalTable: "BankCheckouts",
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
                    PlaidTransferId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Network = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false)
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
                name: "IX_BankCheckoutItems_BankCheckoutId",
                table: "BankCheckoutItems",
                column: "BankCheckoutId");

            migrationBuilder.CreateIndex(
                name: "IX_BankCheckouts_CheckoutId",
                table: "BankCheckouts",
                column: "CheckoutId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_PlaidItemId",
                table: "BankConnections",
                column: "PlaidItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankConnections_UserKey",
                table: "BankConnections",
                column: "UserKey");

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
                name: "BankCheckoutItems");

            migrationBuilder.DropTable(
                name: "BankConnections");

            migrationBuilder.DropTable(
                name: "BankPaymentTransfers");

            migrationBuilder.DropTable(
                name: "BankTransactions");

            migrationBuilder.DropTable(
                name: "BankCheckouts");

            migrationBuilder.DropTable(
                name: "BankPayments");

        }
    }
}
