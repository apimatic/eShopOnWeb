using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations;

public partial class AddMaxioSubscriptionClaims : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "MaxioCustomerResolutions", columns: table => new { UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false), CustomerId = table.Column<int>(type: "int", nullable: true), Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false) }, constraints: table => table.PrimaryKey("PK_MaxioCustomerResolutions", x => x.UserName));
        migrationBuilder.CreateTable(name: "MaxioSubscriptionEnrollments", columns: table => new { Key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false), UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false), PlanHandle = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false), Reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false), SubscriptionId = table.Column<int>(type: "int", nullable: true), Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false) }, constraints: table => table.PrimaryKey("PK_MaxioSubscriptionEnrollments", x => x.Key));
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable(name: "MaxioCustomerResolutions"); migrationBuilder.DropTable(name: "MaxioSubscriptionEnrollments"); }
}
