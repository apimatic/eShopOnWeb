using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

[DbContext(typeof(AppIdentityDbContext))]
[Migration("20260827000000_AddSubscriptionEnrollments")]
public partial class AddSubscriptionEnrollments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SubscriptionEnrollments",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                PlanHandle = table.Column<string>(maxLength: 100, nullable: false),
                SubscriptionReference = table.Column<string>(maxLength: 100, nullable: false),
                Status = table.Column<string>(maxLength: 32, nullable: false),
                MaxioSubscriptionId = table.Column<int>(nullable: true),
                LeaseId = table.Column<string>(maxLength: 32, nullable: true),
                LeaseExpiresAt = table.Column<DateTimeOffset>(nullable: true),
                ConcurrencyToken = table.Column<string>(maxLength: 32, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SubscriptionEnrollments", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionEnrollments_SubscriptionReference",
            table: "SubscriptionEnrollments",
            column: "SubscriptionReference",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionEnrollments_UserId_PlanHandle",
            table: "SubscriptionEnrollments",
            columns: new[] { "UserId", "PlanHandle" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "SubscriptionEnrollments");
}
