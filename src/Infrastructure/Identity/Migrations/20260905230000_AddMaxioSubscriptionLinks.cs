using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

[DbContext(typeof(AppIdentityDbContext))]
[Migration("20260905230000_AddMaxioSubscriptionLinks")]
public partial class AddMaxioSubscriptionLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MaxioCustomerLinks",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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
            name: "MaxioSubscriptionEnrollments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
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
            name: "IX_MaxioCustomerLinks_MaxioCustomerId",
            table: "MaxioCustomerLinks",
            column: "MaxioCustomerId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionEnrollments_MaxioSubscriptionId",
            table: "MaxioSubscriptionEnrollments",
            column: "MaxioSubscriptionId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionEnrollments_UserId_ProductHandle",
            table: "MaxioSubscriptionEnrollments",
            columns: new[] { "UserId", "ProductHandle" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaxioCustomerLinks");
        migrationBuilder.DropTable(name: "MaxioSubscriptionEnrollments");
    }
}
