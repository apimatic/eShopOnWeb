using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaxioCustomers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomers", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_MaxioCustomers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionEnrollments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaxioSubscriptionEnrollments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaxioCustomers_MaxioCustomerId",
                table: "MaxioCustomers",
                column: "MaxioCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionEnrollments_MaxioSubscriptionId",
                table: "MaxioSubscriptionEnrollments",
                column: "MaxioSubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaxioSubscriptionEnrollments_UserId_PlanHandle",
                table: "MaxioSubscriptionEnrollments",
                columns: new[] { "UserId", "PlanHandle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxioCustomers");

            migrationBuilder.DropTable(
                name: "MaxioSubscriptionEnrollments");
        }
    }
}
