using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddsPaymentsAndSavedCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ShipToAddress_State",
                table: "Orders",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(60)",
                oldMaxLength: 60,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthorizedDate",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledDate",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FulfilledDate",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "ItemOrdered_ProductName",
                table: "OrderItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ItemOrdered_PictureUri",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ItemOrdered_CatalogItemId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Catalog",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayPalOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AuthorizationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AuthorizationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AuthorizationExpiration = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RenewedFromAuthorizationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RenewalCount = table.Column<int>(type: "int", nullable: false),
                    AuthorizationAttempts = table.Column<int>(type: "int", nullable: false),
                    CaptureId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CaptureStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CapturedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    FeeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CapturedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CaptureRequestId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    PaymentMethodId = table.Column<int>(type: "int", nullable: true),
                    CardVaultId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PayPalCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Updated = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CardId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayPalCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Alias = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    Brand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Expiry = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true),
                    CardHolderName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    BillingCountry = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PayPalRefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Requested = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Completed = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FeeReturned = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    OrderPaymentId = table.Column<int>(type: "int", nullable: false)
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
                name: "IX_PaymentMethods_BuyerId_Id",
                table: "PaymentMethods",
                columns: new[] { "BuyerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_OrderPaymentId_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "OrderPaymentId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "AuthorizedDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfilledDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "ShipToAddress_State",
                table: "Orders",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<string>(
                name: "ItemOrdered_ProductName",
                table: "OrderItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "ItemOrdered_PictureUri",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<int>(
                name: "ItemOrdered_CatalogItemId",
                table: "OrderItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Catalog",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
