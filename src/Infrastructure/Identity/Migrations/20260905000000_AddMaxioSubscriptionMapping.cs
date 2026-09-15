using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

public partial class AddMaxioSubscriptionMapping : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MaxioSubscriptionMappings",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ApplicationUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                UserReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CustomerReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                SubscriptionReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MaxioSubscriptionId = table.Column<int>(type: "int", nullable: false),
                ProductHandle = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaxioSubscriptionMappings", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionMappings_ApplicationUserId",
            table: "MaxioSubscriptionMappings",
            column: "ApplicationUserId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionMappings_UserReference",
            table: "MaxioSubscriptionMappings",
            column: "UserReference",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaxioSubscriptionMappings");
    }
}
