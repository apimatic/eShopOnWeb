using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAuthorizations_OrderPaymentId",
                table: "PaymentAuthorizations");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "OrderPayments",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAuthorizations_OrderPaymentId_IsCurrent",
                table: "PaymentAuthorizations",
                columns: new[] { "OrderPaymentId", "IsCurrent" },
                unique: true,
                filter: "[IsCurrent] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAuthorizations_OrderPaymentId_IsCurrent",
                table: "PaymentAuthorizations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "OrderPayments");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAuthorizations_OrderPaymentId",
                table: "PaymentAuthorizations",
                column: "OrderPaymentId");
        }
    }
}
