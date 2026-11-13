using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionBillingClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingCustomerLinks",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProviderId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCustomerLinks", x => x.Reference);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionEnrollments",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProviderId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEnrollments", x => x.Reference);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEnrollments_CustomerReference",
                table: "SubscriptionEnrollments",
                column: "CustomerReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingCustomerLinks");

            migrationBuilder.DropTable(
                name: "SubscriptionEnrollments");
        }
    }
}
