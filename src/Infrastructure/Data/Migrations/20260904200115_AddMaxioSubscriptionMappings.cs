using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    MaxioReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomerMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                    MaxioReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerMappings_ApplicationUserId",
                table: "MaxioCustomerMappings",
                column: "ApplicationUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerMappings_MaxioReference",
                table: "MaxioCustomerMappings",
                column: "MaxioReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionMappings_ApplicationUserId_PlanHandle",
                table: "MaxioSubscriptionMappings",
                columns: new[] { "ApplicationUserId", "PlanHandle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionMappings_MaxioReference",
                table: "MaxioSubscriptionMappings",
                column: "MaxioReference",
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
