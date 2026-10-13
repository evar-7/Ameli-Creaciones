using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ameli.Api.Infrastructure.Migrations;

public partial class CustomerIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "first_name", table: "users", type: "nvarchar(80)",
            maxLength: 80, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "last_names", table: "users", type: "nvarchar(160)",
            maxLength: 160, nullable: false, defaultValue: "");

        migrationBuilder.CreateTable(name: "pet_profiles", columns: table => new
        {
            id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            species = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
            breed = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            age_years = table.Column<int>(type: "int", nullable: false),
            weight_kg = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
            height_cm = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
            allergies = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
            is_active = table.Column<bool>(type: "bit", nullable: false),
            photo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
            photo_content_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
            created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            colors_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
            materials_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
            garment_types_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
            styles_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
            preferences_updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_pet_profiles", x => x.id);
            table.ForeignKey("FK_pet_profiles_users_owner_user_id", x => x.owner_user_id,
                "users", "id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(name: "IX_pet_profiles_owner_user_id_is_active_name", table: "pet_profiles",
            columns: new[] { "owner_user_id", "is_active", "name" });

        migrationBuilder.CreateTable(name: "delivery_addresses", columns: table => new
        {
            id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            canton = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            district = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            exact_address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
            reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
            is_default = table.Column<bool>(type: "bit", nullable: false),
            created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_delivery_addresses", x => x.id);
            table.ForeignKey("FK_delivery_addresses_users_owner_user_id", x => x.owner_user_id,
                "users", "id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(name: "IX_delivery_addresses_owner_user_id_is_default", table: "delivery_addresses",
            columns: new[] { "owner_user_id", "is_default" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "delivery_addresses");
        migrationBuilder.DropTable(name: "pet_profiles");
        migrationBuilder.DropColumn(name: "first_name", table: "users");
        migrationBuilder.DropColumn(name: "last_names", table: "users");
    }
}
