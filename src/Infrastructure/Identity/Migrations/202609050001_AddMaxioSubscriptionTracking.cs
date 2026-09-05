using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

public partial class AddMaxioSubscriptionTracking : Migration
{
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
            type: "nvarchar(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MaxioSubscriptionEnrollments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                SubscriptionReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
            name: "IX_AspNetUsers_MaxioCustomerReference",
            table: "AspNetUsers",
            column: "MaxioCustomerReference",
            unique: true,
            filter: "[MaxioCustomerReference] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionEnrollments_SubscriptionReference",
            table: "MaxioSubscriptionEnrollments",
            column: "SubscriptionReference",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionEnrollments_UserId_ProductHandle",
            table: "MaxioSubscriptionEnrollments",
            columns: new[] { "UserId", "ProductHandle" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaxioSubscriptionEnrollments");
        migrationBuilder.DropIndex(name: "IX_AspNetUsers_MaxioCustomerReference", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "MaxioCustomerId", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "MaxioCustomerReference", table: "AspNetUsers");
    }
}
