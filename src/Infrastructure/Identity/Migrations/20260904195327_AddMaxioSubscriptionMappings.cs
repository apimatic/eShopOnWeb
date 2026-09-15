using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaxioCustomerMappings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomerMappings", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxioSubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionMappings_SubscriptionReference",
                table: "MaxioSubscriptionMappings",
                column: "SubscriptionReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionMappings_UserId_ProductHandle",
                table: "MaxioSubscriptionMappings",
                columns: new[] { "UserId", "ProductHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxioCustomerMappings");

            migrationBuilder.DropTable(
                name: "MaxioSubscriptionMappings");
        }
    }
}
