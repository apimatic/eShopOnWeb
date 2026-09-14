using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioBillingMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaxioCustomers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SiteSubdomain = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SiteSubdomain = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MaxioSubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: false),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    IntervalUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NextBillingAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomers_SiteSubdomain_CustomerReference",
                table: "MaxioCustomers",
                columns: new[] { "SiteSubdomain", "CustomerReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomers_UserId_SiteSubdomain",
                table: "MaxioCustomers",
                columns: new[] { "UserId", "SiteSubdomain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptions_SiteSubdomain_SubscriptionReference",
                table: "MaxioSubscriptions",
                columns: new[] { "SiteSubdomain", "SubscriptionReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptions_UserId_SiteSubdomain_ProductHandle",
                table: "MaxioSubscriptions",
                columns: new[] { "UserId", "SiteSubdomain", "ProductHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxioCustomers");

            migrationBuilder.DropTable(
                name: "MaxioSubscriptions");
        }
    }
}
