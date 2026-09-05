using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

[DbContext(typeof(AppIdentityDbContext))]
[Migration("20260905193000_AddMaxioSubscriptionEnrollments")]
public partial class AddMaxioSubscriptionEnrollments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MaxioSubscriptionEnrollments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                SubscriptionReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                MaxioSubscriptionId = table.Column<long>(type: "bigint", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaxioSubscriptionEnrollments", x => x.Id);
            });

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
    }
}
