using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ameli.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InternalAccountsAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "users",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "revision",
                table: "users",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                table: "users",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<string>(
                name: "detail",
                table: "security_events",
                type: "nvarchar(max)",
                maxLength: 6000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1500)",
                oldMaxLength: 1500);

            migrationBuilder.AddColumn<string>(
                name: "actor_email",
                table: "security_events",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "actor_name",
                table: "security_events",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "after_json",
                table: "security_events",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "before_json",
                table: "security_events",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "entity",
                table: "security_events",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "entity_id",
                table: "security_events",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "failed_attempts",
                table: "security_events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "integrity_hash",
                table: "security_events",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "integrity_version",
                table: "security_events",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_legacy",
                table: "security_events",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until_utc",
                table: "security_events",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_started_at_utc",
                table: "security_events",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "module",
                table: "security_events",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "previous_hash",
                table: "security_events",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "session_id",
                table: "security_events",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("UPDATE users SET revision=NEWID(), updated_at_utc=created_at_utc;");
            migrationBuilder.Sql("UPDATE security_events SET module=N'Seguridad', entity=N'users', entity_id=COALESCE(CONVERT(nvarchar(36),subject_user_id),N'');");

            migrationBuilder.CreateTable(
                name: "audit_chain_head",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    last_event_id = table.Column<long>(type: "bigint", nullable: false),
                    record_count = table.Column<long>(type: "bigint", nullable: false),
                    last_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    signature = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    key_id = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    baseline_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_chain_head", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_is_internal_is_active_name",
                table: "users",
                columns: new[] { "is_internal", "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_security_events_actor_user_id_occurred_at_utc",
                table: "security_events",
                columns: new[] { "actor_user_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_security_events_module_occurred_at_utc",
                table: "security_events",
                columns: new[] { "module", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_chain_head");

            migrationBuilder.DropIndex(
                name: "IX_users_is_internal_is_active_name",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_security_events_actor_user_id_occurred_at_utc",
                table: "security_events");

            migrationBuilder.DropIndex(
                name: "IX_security_events_module_occurred_at_utc",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "users");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "users");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "actor_email",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "actor_name",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "after_json",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "before_json",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "entity",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "entity_id",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "failed_attempts",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "integrity_hash",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "integrity_version",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "is_legacy",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "locked_until_utc",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "lockout_started_at_utc",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "module",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "previous_hash",
                table: "security_events");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "security_events");

            migrationBuilder.AlterColumn<string>(
                name: "detail",
                table: "security_events",
                type: "nvarchar(1500)",
                maxLength: 1500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 6000);
        }
    }
}
