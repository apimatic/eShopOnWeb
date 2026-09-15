using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthorizationCreatedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AuthorizationExpiresAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationId",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AuthorizedAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureId",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CapturedAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CapturedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Orders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PayPalFee",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalOrderId",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalOrderStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentAttempt",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "AwaitingPayment");

            migrationBuilder.Sql(
                "UPDATE Orders SET PaymentReference = CONCAT('ESHOP-LEGACY-', CONVERT(varchar(20), Id))");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentReference",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PayPalVaultId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Expiry = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayPalRefundId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentReference",
                table: "Orders",
                column: "PaymentReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_OwnerId_Id",
                table: "PaymentMethods",
                columns: new[] { "OwnerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_PayPalVaultId",
                table: "PaymentMethods",
                column: "PayPalVaultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_OrderId_IdempotencyKey",
                table: "PaymentRefunds",
                columns: new[] { "OrderId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_PayPalRefundId",
                table: "PaymentRefunds",
                column: "PayPalRefundId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizationCreatedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizationExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizationId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizationStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AuthorizedAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CaptureId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CaptureStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CapturedAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CapturedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalOrderStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentAttempt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");

        }
    }
}
