using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionEnrollments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scoped to the subscription table only. The model snapshot also records older, unrelated
            // nullability drift on Orders/OrderItems/Catalog that is intentionally not migrated here.
            migrationBuilder.CreateTable(
                name: "SubscriptionEnrollments",
                columns: table => new
                {
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BillingCustomerId = table.Column<int>(type: "int", nullable: true),
                    BillingSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEnrollments", x => x.BuyerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEnrollments_SubscriptionReference",
                table: "SubscriptionEnrollments",
                column: "SubscriptionReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionEnrollments");
        }
    }
}
