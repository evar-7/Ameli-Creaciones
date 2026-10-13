using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ameli.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssistantCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allergies_declared",
                table: "pet_profiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "care_instructions",
                table: "base_products",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "colors_json",
                table: "base_products",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "fit_notes",
                table: "base_products",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "garment_type",
                table: "base_products",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sku",
                table: "base_products",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "style",
                table: "base_products",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            // Populate unique SKUs before creating the unique index; preserve existing products.
            migrationBuilder.Sql("UPDATE [base_products] SET [sku] = 'AML-' + UPPER(REPLACE(CONVERT(varchar(36), [id]), '-', '')) WHERE [sku] = '';");
            migrationBuilder.Sql("UPDATE [pet_profiles] SET [allergies_declared] = 1 WHERE LEN(LTRIM(RTRIM([allergies]))) > 0;");

            migrationBuilder.CreateTable(
                name: "cart_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    size = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_cart_lines_base_products_product_id",
                        column: x => x.product_id,
                        principalTable: "base_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cart_lines_pet_profiles_pet_id",
                        column: x => x.pet_id,
                        principalTable: "pet_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cart_lines_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cart_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    result_quantity = table.Column<int>(type: "int", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_cart_operations_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    checkout_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tracking_number = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    delivery_snapshot = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_orders", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_orders_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_size_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    min_height_cm = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    max_height_cm = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    min_weight_kg = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    max_weight_kg = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    equivalent = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_size_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_product_size_rules_base_products_product_id",
                        column: x => x.product_id,
                        principalTable: "base_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    product_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    sku = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    size = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    pet_snapshot = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_order_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_order_items_base_products_product_id",
                        column: x => x.product_id,
                        principalTable: "base_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_order_items_customer_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "customer_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_order_items_pet_profiles_pet_id",
                        column: x => x.pet_id,
                        principalTable: "pet_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_base_products_sku",
                table: "base_products",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cart_lines_owner_user_id",
                table: "cart_lines",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_cart_lines_pet_id",
                table: "cart_lines",
                column: "pet_id");

            migrationBuilder.CreateIndex(
                name: "IX_cart_lines_product_id",
                table: "cart_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_cart_operations_owner_user_id_request_id",
                table: "cart_operations",
                columns: new[] { "owner_user_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_order_items_order_id",
                table: "customer_order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_order_items_pet_id",
                table: "customer_order_items",
                column: "pet_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_order_items_product_id",
                table: "customer_order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_orders_number",
                table: "customer_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_orders_owner_user_id_checkout_request_id",
                table: "customer_orders",
                columns: new[] { "owner_user_id", "checkout_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_size_rules_product_id_name",
                table: "product_size_rules",
                columns: new[] { "product_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_lines");

            migrationBuilder.DropTable(
                name: "cart_operations");

            migrationBuilder.DropTable(
                name: "customer_order_items");

            migrationBuilder.DropTable(
                name: "product_size_rules");

            migrationBuilder.DropTable(
                name: "customer_orders");

            migrationBuilder.DropIndex(
                name: "IX_base_products_sku",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "allergies_declared",
                table: "pet_profiles");

            migrationBuilder.DropColumn(
                name: "care_instructions",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "colors_json",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "fit_notes",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "garment_type",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "sku",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "style",
                table: "base_products");
        }
    }
}
