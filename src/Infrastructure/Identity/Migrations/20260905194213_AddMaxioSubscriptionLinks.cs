using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxioCustomerId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaxioCustomerReference",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionLinks_UserId_MaxioSubscriptionId",
                table: "MaxioSubscriptionLinks",
                columns: new[] { "UserId", "MaxioSubscriptionId" },
                unique: true);

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

            migrationBuilder.DropColumn(
                name: "MaxioCustomerId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MaxioCustomerReference",
                table: "AspNetUsers");
        }
    }
}
