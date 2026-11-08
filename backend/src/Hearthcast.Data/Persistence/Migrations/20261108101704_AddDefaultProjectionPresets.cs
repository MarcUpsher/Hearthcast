using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultProjectionPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "ProjectionPresets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionPresets_HouseholdId_Default",
                table: "ProjectionPresets",
                column: "HouseholdId",
                unique: true,
                filter: "\"IsDefault\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectionPresets_HouseholdId_Default",
                table: "ProjectionPresets");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "ProjectionPresets");
        }
    }
}
