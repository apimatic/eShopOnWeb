using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: true),
                    OperationState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnerToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomerLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionEnrollments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    OperationState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OwnerToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionEnrollments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerLinks_MaxioCustomerId",
                table: "MaxioCustomerLinks",
                column: "MaxioCustomerId",
                unique: true,
                filter: "[MaxioCustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerLinks_Reference",
                table: "MaxioCustomerLinks",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerLinks_UserId",
                table: "MaxioCustomerLinks",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionEnrollments_MaxioSubscriptionId",
                table: "MaxioSubscriptionEnrollments",
                column: "MaxioSubscriptionId",
                unique: true,
                filter: "[MaxioSubscriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionEnrollments_Reference",
                table: "MaxioSubscriptionEnrollments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionEnrollments_UserId_ProductHandle",
                table: "MaxioSubscriptionEnrollments",
                columns: new[] { "UserId", "ProductHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxioCustomerLinks");

            migrationBuilder.DropTable(
                name: "MaxioSubscriptionEnrollments");

        }
    }
}
