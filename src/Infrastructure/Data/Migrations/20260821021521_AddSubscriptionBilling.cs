using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PlanName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NextBillingAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_MaxioSubscriptionId",
                table: "SubscriptionRecords",
                column: "MaxioSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_SubscriptionReference",
                table: "SubscriptionRecords",
                column: "SubscriptionReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_UserId_PlanHandle",
                table: "SubscriptionRecords",
                columns: new[] { "UserId", "PlanHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionRecords");

        }
    }
}
