using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

[DbContext(typeof(AppIdentityDbContext))]
[Migration("20260827190000_AddSubscriptionEnrollments")]
public sealed class AddSubscriptionEnrollments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SubscriptionEnrollments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                CustomerReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                SubscriptionReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                MaxioCustomerId = table.Column<int>(type: "int", nullable: true),
                MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                LeaseOwner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LastFailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Version = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SubscriptionEnrollments", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionEnrollments_SubscriptionReference",
            table: "SubscriptionEnrollments",
            column: "SubscriptionReference",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionEnrollments_UserId_ProductHandle",
            table: "SubscriptionEnrollments",
            columns: new[] { "UserId", "ProductHandle" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SubscriptionEnrollments");
}
