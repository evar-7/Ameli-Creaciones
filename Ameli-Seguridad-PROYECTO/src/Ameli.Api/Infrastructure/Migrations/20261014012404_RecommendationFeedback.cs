using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ameli.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recommendation_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pet_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    value = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    reason_code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    reason_text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_feedback", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendation_feedback_base_products_product_id",
                        column: x => x.product_id,
                        principalTable: "base_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recommendation_feedback_pet_profiles_pet_id",
                        column: x => x.pet_id,
                        principalTable: "pet_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recommendation_feedback_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_feedback_owner_user_id_pet_id_product_id_session_id",
                table: "recommendation_feedback",
                columns: new[] { "owner_user_id", "pet_id", "product_id", "session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_feedback_owner_user_id_pet_id_updated_at_utc",
                table: "recommendation_feedback",
                columns: new[] { "owner_user_id", "pet_id", "updated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_feedback_pet_id",
                table: "recommendation_feedback",
                column: "pet_id");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_feedback_product_id",
                table: "recommendation_feedback",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendation_feedback");
        }
    }
}
