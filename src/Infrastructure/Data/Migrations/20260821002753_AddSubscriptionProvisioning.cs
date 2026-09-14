using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "subscription_provisioning_hilo",
                incrementBy: 10);

            migrationBuilder.CreateTable(
                name: "SubscriptionProvisioning",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: true),
                    MaxioSubscriptionId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionProvisioning", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionProvisioning_SubscriptionReference",
                table: "SubscriptionProvisioning",
                column: "SubscriptionReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionProvisioning_UserId_ProductHandle",
                table: "SubscriptionProvisioning",
                columns: new[] { "UserId", "ProductHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionProvisioning");

            migrationBuilder.DropSequence(
                name: "subscription_provisioning_hilo");

        }
    }
}
