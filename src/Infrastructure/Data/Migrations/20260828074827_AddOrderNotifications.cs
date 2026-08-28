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

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Orders",
                type: "rowversion",
                rowVersion: true,
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShopperId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CanonicalNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactNumbers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ContactNumberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResendOfNotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubmissionStatus = table.Column<int>(type: "int", nullable: false),
                    ProviderSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderFrom = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderErrorCode = table.Column<int>(type: "int", nullable: true),
                    ProviderErrorMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ProviderCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRefreshedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastRefreshSucceeded = table.Column<bool>(type: "bit", nullable: false),
                    CancellationState = table.Column<int>(type: "int", nullable: false),
                    RedactionState = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
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
                        name: "FK_OrderNotifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactNumbers_ShopperId_CanonicalNumber",
                table: "ContactNumbers",
                columns: new[] { "ShopperId", "CanonicalNumber" },
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContactNumbers_ShopperId_DeletedAt",
                table: "ContactNumbers",
                columns: new[] { "ShopperId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_ContactNumberId",
                table: "OrderNotifications",
                column: "ContactNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_CreatedAt",
                table: "OrderNotifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_OrderId",
                table: "OrderNotifications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_ProviderSid",
                table: "OrderNotifications",
                column: "ProviderSid",
                unique: true,
                filter: "[ProviderSid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_ResendOfNotificationId_IdempotencyKey",
                table: "OrderNotifications",
                columns: new[] { "ResendOfNotificationId", "IdempotencyKey" },
                unique: true,
                filter: "[ResendOfNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
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
                name: "RowVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");

        }
    }
}
