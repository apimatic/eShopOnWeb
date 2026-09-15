using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackNotificationSender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderFrom",
                table: "OrderNotifications",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderFrom",
                table: "OrderNotifications");
        }
    }
}
