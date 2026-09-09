using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionRecords : Migration
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
                    BuyerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BillingCustomerId = table.Column<int>(type: "int", nullable: false),
                    BillingSubscriptionId = table.Column<int>(type: "int", nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PlanName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PriceInCents = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_BillingSubscriptionId",
                table: "SubscriptionRecords",
                column: "BillingSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_BuyerId_PlanHandle",
                table: "SubscriptionRecords",
                columns: new[] { "BuyerId", "PlanHandle" },
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
