using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

public partial class AddMaxioSubscriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MaxioCustomers",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CustomerId = table.Column<int>(type: "int", nullable: false),
                CustomerReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MaxioCustomers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MaxioSubscriptionEnrollments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                PlanHandle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                SubscriptionReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                SubscriptionId = table.Column<int>(type: "int", nullable: true),
                State = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ProductName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PriceInCents = table.Column<long>(type: "bigint", nullable: true),
                NextBillingAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ProcessingLeaseExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MaxioSubscriptionEnrollments", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_MaxioCustomers_CustomerReference", table: "MaxioCustomers", column: "CustomerReference", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MaxioCustomers_UserId", table: "MaxioCustomers", column: "UserId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MaxioSubscriptionEnrollments_SubscriptionReference", table: "MaxioSubscriptionEnrollments", column: "SubscriptionReference", unique: true);
        migrationBuilder.CreateIndex(name: "IX_MaxioSubscriptionEnrollments_UserId_PlanHandle", table: "MaxioSubscriptionEnrollments", columns: new[] { "UserId", "PlanHandle" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaxioCustomers");
        migrationBuilder.DropTable(name: "MaxioSubscriptionEnrollments");
    }
}
