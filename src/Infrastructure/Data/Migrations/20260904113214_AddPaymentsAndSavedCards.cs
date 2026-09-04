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

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

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
                name: "OrderPaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderRefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderCaptureId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TotalRefundedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RefundReference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RefundedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPaymentRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPaymentRefunds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AuthorizationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AuthorizationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AuthorizedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    AuthorizationExpirationTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizationCreatedTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NetworkTransactionReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UsedVaultTokenId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InvoiceReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaptureId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CaptureStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CapturedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FeeAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_OrderPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    VaultCustomerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VaultTokenId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Expiry = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CardholderName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPaymentRefunds_OrderId",
                table: "OrderPaymentRefunds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_BuyerId",
                table: "SavedPaymentMethods",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_ExternalId",
                table: "SavedPaymentMethods",
                column: "ExternalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderPaymentRefunds");

            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropTable(
                name: "SavedPaymentMethods");

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
