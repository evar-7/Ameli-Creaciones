using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ameli.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GOLLogisticsAndCatalogLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "revision",
                table: "product_categories",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "actor_name",
                table: "order_status_history",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "actor_user_id",
                table: "order_status_history",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "carrier_confirmation",
                table: "customer_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at_utc",
                table: "customer_orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivery_recorded_at_utc",
                table: "customer_orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "delivery_user_id",
                table: "customer_orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_user_name",
                table: "customer_orders",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatched_at_utc",
                table: "customer_orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_tracking_number",
                table: "customer_orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                table: "customer_orders",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "points_enabled",
                table: "customer_orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "preparation_started_at_utc",
                table: "customer_orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "preparation_user_id",
                table: "customer_orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preparation_user_name",
                table: "customer_orders",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "rating_enabled",
                table: "customer_orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "receipt_reference",
                table: "customer_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "packed_quantity",
                table: "customer_order_items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "reserved_quantity",
                table: "customer_order_items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "order_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reported_by_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    missing_items_json = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_incidents", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_incidents_customer_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "customer_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_inventory_alerts",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    detail = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    raised_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_inventory_alerts", x => x.order_id);
                    table.ForeignKey(
                        name: "FK_order_inventory_alerts_customer_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "customer_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_ratings",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stars = table.Column<int>(type: "int", nullable: false),
                    comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_ratings", x => x.order_id);
                    table.ForeignKey(
                        name: "FK_purchase_ratings_customer_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "customer_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_orders_delivery_user_id",
                table: "customer_orders",
                column: "delivery_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_orders_normalized_tracking_number",
                table: "customer_orders",
                column: "normalized_tracking_number",
                unique: true,
                filter: "[normalized_tracking_number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customer_orders_preparation_user_id",
                table: "customer_orders",
                column: "preparation_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_incidents_order_id_request_id",
                table: "order_incidents",
                columns: new[] { "order_id", "request_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_customer_orders_users_delivery_user_id",
                table: "customer_orders",
                column: "delivery_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_customer_orders_users_preparation_user_id",
                table: "customer_orders",
                column: "preparation_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                UPDATE product_categories SET revision = NEWID();
                UPDATE customer_orders SET payment_status = N'Pendiente de verificación';
                UPDATE o SET payment_status = N'Pagado'
                FROM customer_orders o WHERE EXISTS (
                    SELECT 1 FROM payment_attempts p WHERE p.owner_user_id = o.owner_user_id
                    AND p.request_id = o.checkout_request_id AND p.status = N'Aprobado'
                    AND p.amount = o.total AND p.reference = o.payment_reference);
                UPDATE i SET reserved_quantity = i.quantity,
                    packed_quantity = CASE WHEN o.status IN (N'En camino', N'Entregado') THEN i.quantity ELSE 0 END
                FROM customer_order_items i JOIN customer_orders o ON o.id = i.order_id
                WHERE o.status IN (N'Confirmado', N'En preparación', N'En camino', N'Entregado');
                UPDATE o SET dispatched_at_utc = COALESCE(
                    (SELECT MIN(h.occurred_at_utc) FROM order_status_history h WHERE h.order_id = o.id AND h.status = N'En camino'),
                    o.updated_at_utc)
                FROM customer_orders o WHERE o.status IN (N'En camino', N'Entregado');
                UPDATE customer_orders SET delivery_recorded_at_utc = updated_at_utc WHERE status = N'Entregado';
                ;WITH guides AS (
                    SELECT id, UPPER(LTRIM(RTRIM(tracking_number))) AS normalized,
                    ROW_NUMBER() OVER (PARTITION BY UPPER(LTRIM(RTRIM(tracking_number))) ORDER BY created_at_utc, id) AS rn
                    FROM customer_orders WHERE LEN(LTRIM(RTRIM(tracking_number))) > 0)
                UPDATE o SET normalized_tracking_number = g.normalized
                FROM customer_orders o JOIN guides g ON o.id = g.id WHERE g.rn = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customer_orders_users_delivery_user_id",
                table: "customer_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_customer_orders_users_preparation_user_id",
                table: "customer_orders");

            migrationBuilder.DropTable(
                name: "order_incidents");

            migrationBuilder.DropTable(
                name: "order_inventory_alerts");

            migrationBuilder.DropTable(
                name: "purchase_ratings");

            migrationBuilder.DropIndex(
                name: "IX_customer_orders_delivery_user_id",
                table: "customer_orders");

            migrationBuilder.DropIndex(
                name: "IX_customer_orders_normalized_tracking_number",
                table: "customer_orders");

            migrationBuilder.DropIndex(
                name: "IX_customer_orders_preparation_user_id",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "product_categories");

            migrationBuilder.DropColumn(
                name: "actor_name",
                table: "order_status_history");

            migrationBuilder.DropColumn(
                name: "actor_user_id",
                table: "order_status_history");

            migrationBuilder.DropColumn(
                name: "carrier_confirmation",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "delivered_at_utc",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "delivery_recorded_at_utc",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "delivery_user_id",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "delivery_user_name",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "dispatched_at_utc",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "normalized_tracking_number",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "payment_status",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "points_enabled",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "preparation_started_at_utc",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "preparation_user_id",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "preparation_user_name",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "rating_enabled",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "receipt_reference",
                table: "customer_orders");

            migrationBuilder.DropColumn(
                name: "packed_quantity",
                table: "customer_order_items");

            migrationBuilder.DropColumn(
                name: "reserved_quantity",
                table: "customer_order_items");
        }
    }
}
