using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations;

[DbContext(typeof(CatalogContext))]
[Migration("20260828000000_AddOrderNotifications")]
public sealed class AddOrderNotifications : Migration
{
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
            name: "Status",
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
                CanonicalNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ContactNumbers", x => x.Id));

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
                Body = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true),
                ProviderMessageSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ProviderFrom = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                ProviderMessagingServiceSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderDateCreated = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderDateSent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ProviderDateUpdated = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProviderErrorCode = table.Column<int>(type: "int", nullable: true),
                ProviderErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                AttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ContentDisposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ResendOfNotificationId = table.Column<int>(type: "int", nullable: true),
                IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_OrderNotifications", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ContactNumbers_BuyerId_CanonicalNumber",
            table: "ContactNumbers",
            columns: new[] { "BuyerId", "CanonicalNumber" });
        migrationBuilder.CreateIndex(
            name: "IX_ContactNumbers_BuyerId_IsActive",
            table: "ContactNumbers",
            columns: new[] { "BuyerId", "IsActive" });
        migrationBuilder.CreateIndex(
            name: "IX_OrderNotifications_OrderId_CreatedAt",
            table: "OrderNotifications",
            columns: new[] { "OrderId", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_OrderNotifications_ProviderMessageSid",
            table: "OrderNotifications",
            column: "ProviderMessageSid");
        migrationBuilder.CreateIndex(
            name: "IX_OrderNotifications_ResendOfNotificationId_IdempotencyKey",
            table: "OrderNotifications",
            columns: new[] { "ResendOfNotificationId", "IdempotencyKey" },
            unique: true,
            filter: "[ResendOfNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ContactNumbers");
        migrationBuilder.DropTable(name: "OrderNotifications");
        migrationBuilder.DropColumn(name: "CancelledAt", table: "Orders");
        migrationBuilder.DropColumn(name: "DispatchedAt", table: "Orders");
        migrationBuilder.DropColumn(name: "Status", table: "Orders");
    }
}
