using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.eShopWeb.Infrastructure.Data;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations;

[DbContext(typeof(CatalogContext))]
[Migration("20260828000000_AddOrderNotifications")]
public sealed class AddOrderNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CancelledAt", table: "Orders", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DispatchedAt", table: "Orders", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "Status", table: "Orders", type: "nvarchar(32)", maxLength: 32,
            nullable: false, defaultValue: "Placed");

        migrationBuilder.CreateTable(
            name: "ContactNumbers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                CanonicalNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ContactNumbers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "NotificationResends",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                OriginalNotificationId = table.Column<int>(type: "int", nullable: false),
                NewNotificationId = table.Column<int>(type: "int", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_NotificationResends", x => x.Id));

        migrationBuilder.CreateTable(
            name: "OrderNotifications",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrderId = table.Column<int>(type: "int", nullable: false),
                BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ContactNumberId = table.Column<int>(type: "int", nullable: false),
                Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Body = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                SourceNotificationId = table.Column<int>(type: "int", nullable: true),
                IsScheduled = table.Column<bool>(type: "bit", nullable: false),
                ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ProviderSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ProviderErrorCode = table.Column<int>(type: "int", nullable: true),
                ProviderDateCreated = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ProviderDateSent = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ProviderDateUpdated = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastProviderCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ContentDisposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_OrderNotifications", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ContactNumbers_BuyerId_CanonicalNumber", table: "ContactNumbers",
            columns: new[] { "BuyerId", "CanonicalNumber" }, unique: true,
            filter: "[RemovedAt] IS NULL");
        migrationBuilder.CreateIndex(
            name: "IX_NotificationResends_IdempotencyKey", table: "NotificationResends",
            column: "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_OrderNotifications_BuyerId", table: "OrderNotifications", column: "BuyerId");
        migrationBuilder.CreateIndex(
            name: "IX_OrderNotifications_OrderId", table: "OrderNotifications", column: "OrderId");
        migrationBuilder.CreateIndex(
            name: "IX_OrderNotifications_ProviderSid", table: "OrderNotifications",
            column: "ProviderSid", unique: true, filter: "[ProviderSid] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ContactNumbers");
        migrationBuilder.DropTable(name: "NotificationResends");
        migrationBuilder.DropTable(name: "OrderNotifications");
        migrationBuilder.DropColumn(name: "CancelledAt", table: "Orders");
        migrationBuilder.DropColumn(name: "DispatchedAt", table: "Orders");
        migrationBuilder.DropColumn(name: "Status", table: "Orders");
    }
}
