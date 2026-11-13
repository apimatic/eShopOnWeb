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
                name: "BillingCustomerLinks",
                columns: table => new
                {
                    CustomerReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BillingCustomerId = table.Column<int>(type: "int", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCustomerLinks", x => x.CustomerReference);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionClaims",
                columns: table => new
                {
                    CustomerReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BillingSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    ClaimToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionClaims", x => new { x.CustomerReference, x.PlanHandle });
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionClaims_SubscriptionReference",
                table: "SubscriptionClaims",
                column: "SubscriptionReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingCustomerLinks");

            migrationBuilder.DropTable(
                name: "SubscriptionClaims");

        }
    }
}
