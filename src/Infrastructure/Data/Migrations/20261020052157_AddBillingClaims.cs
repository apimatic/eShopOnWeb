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
            // Billing claims only. The snapshot and this migration's target model are the previous snapshot plus the
            // two billing entities: the older nullability drift on Orders/OrderItems/Catalog (non-nullable reference
            // types) is neither applied here nor recorded as applied, so a later `migrations add` still picks it up.
            migrationBuilder.CreateTable(
                name: "BillingCustomerClaims",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCustomerClaims", x => x.Reference);
                });

            migrationBuilder.CreateTable(
                name: "BillingSubscriptionClaims",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSubscriptionClaims", x => x.Reference);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubscriptionClaims_CustomerReference",
                table: "BillingSubscriptionClaims",
                column: "CustomerReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingCustomerClaims");

            migrationBuilder.DropTable(
                name: "BillingSubscriptionClaims");
        }
    }
}
