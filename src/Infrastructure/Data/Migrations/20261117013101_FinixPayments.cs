using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinixPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OrderOperationClaims",
                columns: table => new
                {
                    ClaimKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderOperationClaims", x => x.ClaimKey);
                });

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ShopperId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IdempotencyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AuthorizationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AuthorizationState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AuthorizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CaptureTransferId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CapturedAmountCents = table.Column<long>(type: "bigint", nullable: true),
                    FeeCents = table.Column<long>(type: "bigint", nullable: true),
                    NetCents = table.Column<long>(type: "bigint", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefundedCents = table.Column<long>(type: "bigint", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "RefundLocks",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundLocks", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "RefundRecords",
                columns: table => new
                {
                    RefundKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    RefundId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExistingReversalIds = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundRecords", x => x.RefundKey);
                });

            migrationBuilder.CreateTable(
                name: "SavedCardClaims",
                columns: table => new
                {
                    ClaimKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PaymentInstrumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCardClaims", x => x.ClaimKey);
                });

            migrationBuilder.CreateTable(
                name: "SavedCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShopperId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PaymentInstrumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastFour = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ExpirationMonth = table.Column<int>(type: "int", nullable: true),
                    ExpirationYear = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShopperFinixIdentities",
                columns: table => new
                {
                    ShopperId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IdentityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopperFinixIdentities", x => x.ShopperId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_AuthorizationId",
                table: "OrderPayments",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_CaptureTransferId",
                table: "OrderPayments",
                column: "CaptureTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundRecords_OrderId_IdempotencyKey",
                table: "RefundRecords",
                columns: new[] { "OrderId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundRecords_RefundId",
                table: "RefundRecords",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedCards_ShopperId_PaymentInstrumentId",
                table: "SavedCards",
                columns: new[] { "ShopperId", "PaymentInstrumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderOperationClaims");

            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropTable(
                name: "RefundLocks");

            migrationBuilder.DropTable(
                name: "RefundRecords");

            migrationBuilder.DropTable(
                name: "SavedCardClaims");

            migrationBuilder.DropTable(
                name: "SavedCards");

            migrationBuilder.DropTable(
                name: "ShopperFinixIdentities");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");
        }
    }
}
