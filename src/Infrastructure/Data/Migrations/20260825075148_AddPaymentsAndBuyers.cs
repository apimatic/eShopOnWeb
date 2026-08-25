using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsAndBuyers : Migration
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
                name: "Buyers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdentityGuid = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buyers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayPalOrderId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PayPalAuthorizationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthorizationStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthorizationExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AuthorizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PayPalCaptureId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CaptureStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CapturedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PayPalFeeAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethod",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<int>(type: "int", nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CardId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Expiry = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethod", x => new { x.BuyerId, x.Id });
                    table.ForeignKey(
                        name: "FK_PaymentMethod_Buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "Buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Refund",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayPalRefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refund", x => new { x.PaymentId, x.Id });
                    table.ForeignKey(
                        name: "FK_Refund_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Buyers_IdentityGuid",
                table: "Buyers",
                column: "IdentityGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMethod");

            migrationBuilder.DropTable(
                name: "Refund");

            migrationBuilder.DropTable(
                name: "Buyers");

            migrationBuilder.DropTable(
                name: "Payments");

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
