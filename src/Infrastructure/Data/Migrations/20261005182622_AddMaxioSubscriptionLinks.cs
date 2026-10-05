using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaxioCustomerLinks",
                columns: table => new
                {
                    MaxioReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomerLinks", x => x.MaxioReference);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionLinks",
                columns: table => new
                {
                    MaxioReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionLinks", x => x.MaxioReference);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionLinks_UserId_ProductHandle",
                table: "MaxioSubscriptionLinks",
                columns: new[] { "UserId", "ProductHandle" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxioSubscriptionLinks");

            migrationBuilder.DropTable(
                name: "MaxioCustomerLinks");
        }
    }
}