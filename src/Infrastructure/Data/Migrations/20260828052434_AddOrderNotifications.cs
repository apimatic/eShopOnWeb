using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FulfillmentStatus",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ContactNumbers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactNumbers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationResendClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceNotificationId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProducedNotificationId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationResendClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContactNumberId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SourceNotificationId = table.Column<int>(type: "int", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true),
                    ProviderMessageSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderErrorCode = table.Column<int>(type: "int", nullable: true),
                    ProviderErrorMessage = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProviderDateCreated = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderDateSent = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContentDisposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSyncFailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderNotifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactNumbers_BuyerId_Number",
                table: "ContactNumbers",
                columns: new[] { "BuyerId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationResendClaims_SourceNotificationId_IdempotencyKey",
                table: "NotificationResendClaims",
                columns: new[] { "SourceNotificationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_OrderId_CreatedAt",
                table: "OrderNotifications",
                columns: new[] { "OrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_ProviderMessageSid",
                table: "OrderNotifications",
                column: "ProviderMessageSid",
                unique: true,
                filter: "[ProviderMessageSid] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactNumbers");

            migrationBuilder.DropTable(
                name: "NotificationResendClaims");

            migrationBuilder.DropTable(
                name: "OrderNotifications");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "Orders");

        }
    }
}
