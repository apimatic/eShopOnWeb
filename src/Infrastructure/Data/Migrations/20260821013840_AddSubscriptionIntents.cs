using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionIntents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProviderSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    PlanName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PlanHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: true),
                    ProviderState = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NextBillingAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionIntents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionIntents_ProviderReference",
                table: "SubscriptionIntents",
                column: "ProviderReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionIntents_UserId_ProductId",
                table: "SubscriptionIntents",
                columns: new[] { "UserId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionIntents");

        }
    }
}
