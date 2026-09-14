using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaxioCustomerMappings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomerMappings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_MaxioCustomerMappings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UniquenessToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MaxioCustomerId = table.Column<long>(type: "bigint", nullable: false),
                    MaxioSubscriptionId = table.Column<long>(type: "bigint", nullable: true),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    NextBillingDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaxioSubscriptionMappings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerMappings_CustomerReference",
                table: "MaxioCustomerMappings",
                column: "CustomerReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomerMappings_MaxioCustomerId",
                table: "MaxioCustomerMappings",
                column: "MaxioCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionMappings_MaxioSubscriptionId",
                table: "MaxioSubscriptionMappings",
                column: "MaxioSubscriptionId",
                unique: true,
                filter: "[MaxioSubscriptionId] IS NOT NULL");

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
                name: "MaxioCustomerMappings");

            migrationBuilder.DropTable(
                name: "MaxioSubscriptionMappings");
        }
    }
}
