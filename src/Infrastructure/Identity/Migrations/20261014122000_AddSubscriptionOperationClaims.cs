using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

[DbContext(typeof(AppIdentityDbContext))]
[Migration("20261014122000_AddSubscriptionOperationClaims")]
public partial class AddSubscriptionOperationClaims : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SubscriptionOperationClaims",
            columns: table => new
            {
                Key = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                Reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                MaxioCustomerId = table.Column<int>(type: "int", nullable: true),
                MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_SubscriptionOperationClaims", x => x.Key));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SubscriptionOperationClaims");
}
