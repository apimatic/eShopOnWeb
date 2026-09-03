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

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Placed");

            migrationBuilder.CreateTable(
                name: "ContactNumbers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
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
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContactNumberId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderMessageSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderErrorCode = table.Column<int>(type: "int", nullable: true),
                    ProviderErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ProviderCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceNotificationId = table.Column<int>(type: "int", nullable: true),
                    ResendIdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CancellationRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationCompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ContentDisposedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
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
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderNotifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactNumbers_BuyerId_DeletedAt",
                table: "ContactNumbers",
                columns: new[] { "BuyerId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContactNumbers_BuyerId_PhoneNumber",
                table: "ContactNumbers",
                columns: new[] { "BuyerId", "PhoneNumber" },
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_ContactNumberId_ScheduledFor_CancellationCompletedAt",
                table: "OrderNotifications",
                columns: new[] { "ContactNumberId", "ScheduledFor", "CancellationCompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_CreatedAt",
                table: "OrderNotifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_OrderId_BuyerId",
                table: "OrderNotifications",
                columns: new[] { "OrderId", "BuyerId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_ProviderMessageSid",
                table: "OrderNotifications",
                column: "ProviderMessageSid",
                unique: true,
                filter: "[ProviderMessageSid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_SourceNotificationId_ResendIdempotencyKey",
                table: "OrderNotifications",
                columns: new[] { "SourceNotificationId", "ResendIdempotencyKey" },
                unique: true,
                filter: "[SourceNotificationId] IS NOT NULL AND [ResendIdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
