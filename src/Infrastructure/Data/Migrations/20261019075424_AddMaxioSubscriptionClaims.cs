using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxioSubscriptionClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaxioCustomerClaims",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioCustomerClaims", x => x.Reference);
                });

            migrationBuilder.CreateTable(
                name: "MaxioSubscriptionClaims",
                columns: table => new
                {
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductHandle = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MaxioCustomerId = table.Column<int>(type: "int", nullable: false),
                    MaxioSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaxioSubscriptionClaims", x => x.UserName);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaxioCustomerClaims");

            migrationBuilder.DropTable(
                name: "MaxioSubscriptionClaims");
        }
    }
}
