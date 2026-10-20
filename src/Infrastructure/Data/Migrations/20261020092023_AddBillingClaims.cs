using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingCustomerClaims",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BillingCustomerId = table.Column<int>(type: "int", nullable: true),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCustomerClaims", x => x.Reference);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionClaims",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    BillingSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionClaims", x => x.Reference);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCustomerClaims_BuyerId",
                table: "BillingCustomerClaims",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionClaims_BuyerId",
                table: "SubscriptionClaims",
                column: "BuyerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingCustomerClaims");

            migrationBuilder.DropTable(
                name: "SubscriptionClaims");
        }
    }
}
