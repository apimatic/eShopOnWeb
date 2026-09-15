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
                name: "MaxioSubscriptionMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    MaxioSubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionMappings_MaxioSubscriptionId",
                table: "MaxioSubscriptionMappings",
                column: "MaxioSubscriptionId",
                unique: true);

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
                name: "MaxioSubscriptionMappings");
        }
    }
}
