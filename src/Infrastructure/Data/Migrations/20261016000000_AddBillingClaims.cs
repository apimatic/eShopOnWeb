using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations;

public partial class AddBillingClaims : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("BillingClaims", columns: table => new
        {
            Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
            PlanHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
            Reference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            ProviderId = table.Column<int>(type: "int", nullable: true),
            State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_BillingClaims", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("BillingClaims");
}
