using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations;

[DbContext(typeof(CatalogContext))]
[Migration("20261016000000_AddBillingClaims")]
public class AddBillingClaims : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("BillingClaims", table => new
        {
            Id = table.Column<string>(maxLength: 80, nullable: false),
            Kind = table.Column<string>(maxLength: 20, nullable: false),
            CustomerReference = table.Column<string>(maxLength: 80, nullable: false),
            ProductHandle = table.Column<string>(maxLength: 255, nullable: true),
            ProviderId = table.Column<int>(nullable: true),
            LastCheckedAt = table.Column<DateTimeOffset>(nullable: true),
            CreatedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_BillingClaims", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("BillingClaims");
}
