using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Identity.Migrations;

[DbContext(typeof(AppIdentityDbContext))]
[Migration("20260827000000_AddMaxioSubscriptions")]
public partial class AddMaxioSubscriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MaxioSubscriptionLinks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                SubscriptionReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                LeaseOwner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaxioSubscriptionLinks", x => x.Id);
                table.ForeignKey(
                    name: "FK_MaxioSubscriptionLinks_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionLinks_SubscriptionReference",
            table: "MaxioSubscriptionLinks",
            column: "SubscriptionReference",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MaxioSubscriptionLinks_UserId_ProductHandle",
            table: "MaxioSubscriptionLinks",
            columns: new[] { "UserId", "ProductHandle" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MaxioSubscriptionLinks");
    }
}
