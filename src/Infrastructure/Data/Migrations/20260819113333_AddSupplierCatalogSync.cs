using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Microsoft.eShopWeb.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierCatalogSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogSyncs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemsFound = table.Column<int>(type: "int", nullable: false),
                    ItemsImported = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSyncs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCatalogItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    ExternalKey = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    NameKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CatalogItemId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ListingUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogSyncs_SupplierId",
                table: "CatalogSyncs",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCatalogItems_SupplierId_ExternalKey",
                table: "SupplierCatalogItems",
                columns: new[] { "SupplierId", "ExternalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCatalogItems_SupplierId_NameKey",
                table: "SupplierCatalogItems",
                columns: new[] { "SupplierId", "NameKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogSyncs");

            migrationBuilder.DropTable(
                name: "SupplierCatalogItems");

            migrationBuilder.DropTable(
                name: "Suppliers");
        }
    }
}
