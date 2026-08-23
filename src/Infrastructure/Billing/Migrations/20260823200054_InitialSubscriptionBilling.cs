using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Billing.Migrations
{
    /// <inheritdoc />
    public partial class InitialSubscriptionBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionEnrollments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntegrationScope = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: true),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastFailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEnrollments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEnrollments_IntegrationScope_CustomerReference",
                table: "SubscriptionEnrollments",
                columns: new[] { "IntegrationScope", "CustomerReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEnrollments_IntegrationScope_SubscriptionReference",
                table: "SubscriptionEnrollments",
                columns: new[] { "IntegrationScope", "SubscriptionReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEnrollments_IntegrationScope_UserId_ProductHandle",
                table: "SubscriptionEnrollments",
                columns: new[] { "IntegrationScope", "UserId", "ProductHandle" },
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
