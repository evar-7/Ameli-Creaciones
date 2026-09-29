using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ameli.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CatalogBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    normalized_name = table.Column<string>(type: "varchar(120)", unicode: false, maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    normalized_name = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    contact_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "base_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    normalized_name = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    category_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    species = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    material = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    icon = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    stock_quantity = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_base_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_base_products_product_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "product_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_base_products_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_base_products_category_id_supplier_id_is_active",
                table: "base_products",
                columns: new[] { "category_id", "supplier_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_base_products_normalized_name",
                table: "base_products",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_base_products_species_is_active_name",
                table: "base_products",
                columns: new[] { "species", "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_base_products_supplier_id",
                table: "base_products",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_is_active_name",
                table: "product_categories",
                columns: new[] { "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_normalized_name",
                table: "product_categories",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_is_active_name",
                table: "suppliers",
                columns: new[] { "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_normalized_email",
                table: "suppliers",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_normalized_name",
                table: "suppliers",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "base_products");

            migrationBuilder.DropTable(
                name: "product_categories");

            migrationBuilder.DropTable(
                name: "suppliers");
        }
    }
}
