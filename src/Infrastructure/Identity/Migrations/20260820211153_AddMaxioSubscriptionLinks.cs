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
            migrationBuilder.CreateTable(
                name: "MaxioCustomerLinks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomerLinks", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_MaxioCustomerLinks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionLinks",
                columns: table => new
                {
                    MaxioSubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionLinks", x => x.MaxioSubscriptionId);
                    table.ForeignKey(
                        name: "FK_MaxioSubscriptionLinks_MaxioCustomerLinks_UserId",
                        column: x => x.UserId,
                        principalTable: "MaxioCustomerLinks",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
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
