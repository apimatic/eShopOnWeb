using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

[DbContext(typeof(AppIdentityDbContext))]
[Migration("20260905100000_AddMaxioSubscriptionEnrollment")]
public partial class AddMaxioSubscriptionEnrollment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MaxioSubscriptionEnrollments",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                PlanHandle = table.Column<string>(maxLength: 255, nullable: false),
                MaxioCustomerId = table.Column<long>(nullable: false),
                MaxioSubscriptionId = table.Column<long>(nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaxioSubscriptionEnrollments", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionEnrollments_UserId_PlanHandle",
            table: "MaxioSubscriptionEnrollments",
            columns: new[] { "UserId", "PlanHandle" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaxioSubscriptionEnrollments");
    }
}
