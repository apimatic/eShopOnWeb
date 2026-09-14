using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaxioCustomerLinks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomerLinks", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionLinks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionLinks", x => new { x.UserId, x.ProductHandle });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerLinks_CustomerReference",
                table: "MaxioCustomerLinks",
                column: "CustomerReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerLinks_MaxioCustomerId",
                table: "MaxioCustomerLinks",
                column: "MaxioCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionLinks_MaxioSubscriptionId",
                table: "MaxioSubscriptionLinks",
                column: "MaxioSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionLinks_SubscriptionReference",
                table: "MaxioSubscriptionLinks",
                column: "SubscriptionReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxioCustomerLinks");

            migrationBuilder.DropTable(
                name: "MaxioSubscriptionLinks");
        }
    }
}
