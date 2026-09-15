using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

public partial class AddMaxioSubscriptionMappings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MaxioCustomerMappings",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                MaxioCustomerId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaxioCustomerMappings", x => x.UserId);
            });

        migrationBuilder.CreateTable(
            name: "MaxioSubscriptionMappings",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                ProductHandle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Reference = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaxioSubscriptionMappings", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionMappings_MaxioSubscriptionId",
            table: "MaxioSubscriptionMappings",
            column: "MaxioSubscriptionId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionMappings_UserId_Reference",
            table: "MaxioSubscriptionMappings",
            columns: new[] { "UserId", "Reference" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaxioCustomerMappings");
        migrationBuilder.DropTable(name: "MaxioSubscriptionMappings");
    }
}
