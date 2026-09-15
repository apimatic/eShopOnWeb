using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.eShopWeb.Infrastructure.Data;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations;

[DbContext(typeof(CatalogContext))]
[Migration("20260823000000_AddOrderNotifications")]
public partial class AddOrderNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Status",
            table: "Orders",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Submitted");

        migrationBuilder.CreateTable(
            name: "ContactNumbers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                CanonicalNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CountryCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Body = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true),
                ContentRedacted = table.Column<bool>(type: "bit", nullable: false),
                DestinationNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                ContactNumberId = table.Column<int>(type: "int", nullable: true),
                SourceNotificationId = table.Column<int>(type: "int", nullable: true),
                ProviderMessageSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                ProviderErrorCode = table.Column<int>(type: "int", nullable: true),
                ProviderErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderNotifications", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "NotificationIdempotencyRecords",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SourceNotificationId = table.Column<int>(type: "int", nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ResultNotificationId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationIdempotencyRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ContactNumbers_BuyerId_CanonicalNumber",
            table: "ContactNumbers",
            columns: new[] { "BuyerId", "CanonicalNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrderNotifications_OrderId",
            table: "OrderNotifications",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderNotifications_ProviderMessageSid",
            table: "OrderNotifications",
            column: "ProviderMessageSid");

        migrationBuilder.CreateIndex(
            name: "IX_NotificationIdempotencyRecords_SourceNotificationId_IdempotencyKey",
            table: "NotificationIdempotencyRecords",
            columns: new[] { "SourceNotificationId", "IdempotencyKey" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ContactNumbers");
        migrationBuilder.DropTable(name: "OrderNotifications");
        migrationBuilder.DropTable(name: "NotificationIdempotencyRecords");
        migrationBuilder.DropColumn(name: "Status", table: "Orders");
    }
}
