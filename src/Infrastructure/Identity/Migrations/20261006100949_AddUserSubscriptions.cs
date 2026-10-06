using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NextBillingAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_MaxioSubscriptionId",
                table: "UserSubscriptions",
                column: "MaxioSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId_ProductHandle",
                table: "UserSubscriptions",
                columns: new[] { "UserId", "ProductHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSubscriptions");
        }
    }
}
