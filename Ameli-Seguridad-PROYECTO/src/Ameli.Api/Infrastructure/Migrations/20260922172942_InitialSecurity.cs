using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ameli.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    protected_body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recovery_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    origin = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "security_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    subject_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    origin = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    detail = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    role_name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    is_internal = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    failed_access_count = table.Column<int>(type: "int", nullable: false),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_login_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    security_version = table.Column<int>(type: "int", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_roles_role_name",
                        column: x => x.role_name,
                        principalTable: "roles",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "auth_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    security_version = table.Column<int>(type: "int", nullable: false),
                    refresh_token_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    absolute_expires_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    device = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_auth_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "password_resets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_resets", x => x.id);
                    table.ForeignKey(
                        name: "FK_password_resets_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "name", "is_active" },
                values: new object[,]
                {
                    { "Administrador", true },
                    { "Cliente", true },
                    { "Logística", true }
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_sessions_refresh_token_hash",
                table: "auth_sessions",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_sessions_user_id_revoked_at_utc",
                table: "auth_sessions",
                columns: new[] { "user_id", "revoked_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_sent_at_utc_next_attempt_at_utc",
                table: "email_outbox",
                columns: new[] { "sent_at_utc", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_password_resets_token_hash",
                table: "password_resets",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_password_resets_user_id_created_at_utc",
                table: "password_resets",
                columns: new[] { "user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_attempts_account_key_created_at_utc",
                table: "recovery_attempts",
                columns: new[] { "account_key", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_attempts_origin_created_at_utc",
                table: "recovery_attempts",
                columns: new[] { "origin", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_security_events_occurred_at_utc",
                table: "security_events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_role_name",
                table: "users",
                column: "role_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_sessions");

            migrationBuilder.DropTable(
                name: "email_outbox");

            migrationBuilder.DropTable(
                name: "password_resets");

            migrationBuilder.DropTable(
                name: "recovery_attempts");

            migrationBuilder.DropTable(
                name: "security_events");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
