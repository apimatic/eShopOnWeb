using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionSignups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionSignups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PlanHandle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    CustomerReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SubscriptionReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CustomerUniquenessToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CustomerCreateFirstSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CustomerCreateConflicted = table.Column<bool>(type: "bit", nullable: false),
                    SubscriptionUniquenessToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SubscriptionCreateFirstSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SubscriptionCreateConflicted = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BillingSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    ActionLink = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSignups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSignups_Status",
                table: "SubscriptionSignups",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSignups_UserId_PlanHandle",
                table: "SubscriptionSignups",
                columns: new[] { "UserId", "PlanHandle" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionSignups");
        }
    }
}
