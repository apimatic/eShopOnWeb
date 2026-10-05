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
                name: "BillingCustomers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BillingReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BillingCustomerId = table.Column<int>(type: "int", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCustomers", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionEnrollments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    BillingReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BillingSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEnrollments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEnrollments_UserId",
                table: "SubscriptionEnrollments",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingCustomers");

            migrationBuilder.DropTable(
                name: "SubscriptionEnrollments");
        }
    }
}
