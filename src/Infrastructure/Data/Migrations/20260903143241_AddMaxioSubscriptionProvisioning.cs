using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionProvisioningIntents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    LeaseToken = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionProvisioningIntents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionProvisioningIntents_SubscriptionReference",
                table: "SubscriptionProvisioningIntents",
                column: "SubscriptionReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionProvisioningIntents_UserReference_ProductHandle",
                table: "SubscriptionProvisioningIntents",
                columns: new[] { "UserReference", "ProductHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionProvisioningIntents");
        }
    }
}
