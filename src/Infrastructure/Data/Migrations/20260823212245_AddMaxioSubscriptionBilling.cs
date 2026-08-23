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
                name: "MaxioCustomers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MaxioCustomerReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecurringSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    ProviderState = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NextBillingAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MaxioSubscriptionReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    OperationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SendStarted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomers_ApplicationUserId",
                table: "MaxioCustomers",
                column: "ApplicationUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomers_MaxioCustomerId",
                table: "MaxioCustomers",
                column: "MaxioCustomerId",
                unique: true,
                filter: "[MaxioCustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomers_MaxioCustomerReference",
                table: "MaxioCustomers",
                column: "MaxioCustomerReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSubscriptions_ApplicationUserId_ProductHandle",
                table: "RecurringSubscriptions",
                columns: new[] { "ApplicationUserId", "ProductHandle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSubscriptions_MaxioSubscriptionId",
                table: "RecurringSubscriptions",
                column: "MaxioSubscriptionId",
                unique: true,
                filter: "[MaxioSubscriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSubscriptions_MaxioSubscriptionReference",
                table: "RecurringSubscriptions",
                column: "MaxioSubscriptionReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxioCustomers");

            migrationBuilder.DropTable(
                name: "RecurringSubscriptions");

        }
    }
}
