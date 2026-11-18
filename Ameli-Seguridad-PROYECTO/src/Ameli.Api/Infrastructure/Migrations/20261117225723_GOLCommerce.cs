using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ameli.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GOLCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "discount",
                table: "customer_orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "payment_mode",
                table: "customer_orders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "payment_reference",
                table: "customer_orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "subtotal",
                table: "customer_orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax",
                table: "customer_orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "revision",
                table: "cart_lines",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<byte[]>(
                name: "image",
                table: "base_products",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_content_type",
                table: "base_products",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "minimum_stock",
                table: "base_products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "revision",
                table: "base_products",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                table: "base_products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "order_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    tracking_number = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_status_history_customer_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "customer_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_attempts_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_status_history_order_id_occurred_at_utc",
                table: "order_status_history",
                columns: new[] { "order_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_owner_user_id_request_id",
                table: "payment_attempts",
                columns: new[] { "owner_user_id", "request_id" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE base_products SET revision = NEWID();
                UPDATE cart_lines SET revision = NEWID();
                UPDATE customer_orders SET subtotal = total WHERE subtotal = 0;
                UPDATE customer_orders SET status = N'En camino' WHERE status = N'Enviado';
                INSERT INTO order_status_history (id, order_id, status, occurred_at_utc, carrier, tracking_number)
                SELECT NEWID(), id, N'Confirmado', created_at_utc, N'', N'' FROM customer_orders;
                INSERT INTO order_status_history (id, order_id, status, occurred_at_utc, carrier, tracking_number)
                SELECT NEWID(), id, status, updated_at_utc, carrier, tracking_number
                FROM customer_orders WHERE status <> N'Confirmado';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_status_history");

            migrationBuilder.DropTable(
                name: "payment_attempts");

            migrationBuilder.DropColumn(
                name: "discount",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "payment_mode",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "payment_reference",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "subtotal",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "tax",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "cart_lines");

            migrationBuilder.DropColumn(
                name: "image",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "image_content_type",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "minimum_stock",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "base_products");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "base_products");
        }
    }
}
