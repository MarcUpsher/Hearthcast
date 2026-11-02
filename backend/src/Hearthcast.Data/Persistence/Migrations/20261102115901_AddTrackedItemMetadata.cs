using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedItemMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FundName",
                table: "TrackedItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstitutionName",
                table: "TrackedItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FundName",
                table: "ItemSnapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstitutionName",
                table: "ItemSnapshots",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FundName",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "InstitutionName",
                table: "TrackedItems");

            migrationBuilder.DropColumn(
                name: "FundName",
                table: "ItemSnapshots");

            migrationBuilder.DropColumn(
                name: "InstitutionName",
                table: "ItemSnapshots");
        }
    }
}
