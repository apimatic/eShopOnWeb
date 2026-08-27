using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrderNotifications : Migration
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

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Placed");

            migrationBuilder.CreateTable(
                name: "ContactNumbers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CanonicalNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactNumbers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ContactNumberId = table.Column<int>(type: "int", nullable: false),
                    SourceNotificationId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true),
                    ProviderMessageSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderErrorCode = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRefreshedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRefreshFailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContentDisposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationPending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderNotifications_ContactNumbers_ContactNumberId",
                        column: x => x.ContactNumberId,
                        principalTable: "ContactNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderNotifications_OrderNotifications_SourceNotificationId",
                        column: x => x.SourceNotificationId,
                        principalTable: "OrderNotifications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderNotifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationResends",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceNotificationId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResultNotificationId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationResends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationResends_OrderNotifications_ResultNotificationId",
                        column: x => x.ResultNotificationId,
                        principalTable: "OrderNotifications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NotificationResends_OrderNotifications_SourceNotificationId",
                        column: x => x.SourceNotificationId,
                        principalTable: "OrderNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactNumbers_BuyerId_CanonicalNumber",
                table: "ContactNumbers",
                columns: new[] { "BuyerId", "CanonicalNumber" },
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationResends_ResultNotificationId",
                table: "NotificationResends",
                column: "ResultNotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationResends_SourceNotificationId_IdempotencyKey",
                table: "NotificationResends",
                columns: new[] { "SourceNotificationId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_ContactNumberId_Kind_ProviderStatus",
                table: "OrderNotifications",
                columns: new[] { "ContactNumberId", "Kind", "ProviderStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_OrderId",
                table: "OrderNotifications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_ProviderMessageSid",
                table: "OrderNotifications",
                column: "ProviderMessageSid",
                unique: true,
                filter: "[ProviderMessageSid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_SourceNotificationId",
                table: "OrderNotifications",
                column: "SourceNotificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationResends");

            migrationBuilder.DropTable(
                name: "OrderNotifications");

            migrationBuilder.DropTable(
                name: "ContactNumbers");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");

        }
    }
}
