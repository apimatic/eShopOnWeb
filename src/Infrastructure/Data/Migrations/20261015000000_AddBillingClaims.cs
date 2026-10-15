using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations;

[DbContext(typeof(CatalogContext))]
[Migration("20261015000000_AddBillingClaims")]
public class AddBillingClaims : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("BillingClaims", table => new
        {
            Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
            Site = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
            Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
            Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
            ProviderId = table.Column<int>(type: "int", nullable: true),
            CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_BillingClaims", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("BillingClaims");
}
