using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsOrderNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purely additive: this feature only introduces the ContactNumbers and
            // OrderNotifications tables. (Unrelated AlterColumn drift emitted by newer EF tooling
            // against the older snapshot was removed by hand to keep this migration surgical.)
            migrationBuilder.CreateTable(
                name: "ContactNumbers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
                    Type = table.Column<int>(type: "int", nullable: false),
                    ToPhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MessageSid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ErrorCode = table.Column<int>(type: "int", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsScheduledFollowUp = table.Column<bool>(type: "bit", nullable: false),
                    ContentRedacted = table.Column<bool>(type: "bit", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderDateSent = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactNumbers_BuyerId",
                table: "ContactNumbers",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_BuyerId",
                table: "OrderNotifications",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_IdempotencyKey",
                table: "OrderNotifications",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_MessageSid",
                table: "OrderNotifications",
                column: "MessageSid");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_OrderId",
                table: "OrderNotifications",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactNumbers");

            migrationBuilder.DropTable(
                name: "OrderNotifications");
        }
    }
}
