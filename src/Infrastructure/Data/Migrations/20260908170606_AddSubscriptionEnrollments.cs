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
            migrationBuilder.CreateTable(
                name: "SubscriptionEnrollments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: true),
                    MaxioSubscriptionReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProductPriceInCents = table.Column<long>(type: "bigint", nullable: true),
                    NextBillingDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEnrollments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEnrollments_BuyerId_PlanHandle",
                table: "SubscriptionEnrollments",
                columns: new[] { "BuyerId", "PlanHandle" },
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
