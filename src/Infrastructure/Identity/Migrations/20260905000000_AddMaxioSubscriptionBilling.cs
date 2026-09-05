using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

public partial class AddMaxioSubscriptionBilling : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FirstName",
            table: "AspNetUsers",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastName",
            table: "AspNetUsers",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MaxioCustomerLinks",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                CustomerReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaxioCustomerLinks", x => x.Id);
                table.ForeignKey("FK_MaxioCustomerLinks_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MaxioSubscriptionEnrollments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ProductHandle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                SubscriptionReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaxioSubscriptionEnrollments", x => x.Id);
                table.ForeignKey("FK_MaxioSubscriptionEnrollments_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_MaxioCustomerLinks_CustomerReference", table: "MaxioCustomerLinks", column: "CustomerReference", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MaxioCustomerLinks_UserId", table: "MaxioCustomerLinks", column: "UserId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MaxioSubscriptionEnrollments_SubscriptionReference", table: "MaxioSubscriptionEnrollments", column: "SubscriptionReference", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MaxioSubscriptionEnrollments_UserId_ProductHandle", table: "MaxioSubscriptionEnrollments", columns: new[] { "UserId", "ProductHandle" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaxioCustomerLinks");
        migrationBuilder.DropTable(name: "MaxioSubscriptionEnrollments");
        migrationBuilder.DropColumn(name: "FirstName", table: "AspNetUsers");
        migrationBuilder.DropColumn(name: "LastName", table: "AspNetUsers");
    }
}
