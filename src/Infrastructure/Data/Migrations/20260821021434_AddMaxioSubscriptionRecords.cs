using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: true),
                    MaxioSubscriptionId = table.Column<long>(type: "bigint", nullable: true),
                    CreationStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_MaxioSubscriptionId",
                table: "SubscriptionRecords",
                column: "MaxioSubscriptionId",
                unique: true,
                filter: "[MaxioSubscriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_SubscriptionReference",
                table: "SubscriptionRecords",
                column: "SubscriptionReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRecords_UserId_ProductHandle",
                table: "SubscriptionRecords",
                columns: new[] { "UserId", "ProductHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionRecords");

        }
    }
}
