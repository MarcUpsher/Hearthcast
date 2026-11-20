using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpendingAdditionalCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdditionalCost",
                table: "SpendingLines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingLines_AdditionalCost",
                table: "SpendingLines",
                sql: "NOT \"IsAdditionalCost\" OR (\"NetAmount\" > 0 AND \"Description\" IS NOT NULL\n    AND \"SpendingItemId\" IS NULL AND \"UnitKey\" IS NULL AND \"UnitLabel\" IS NULL\n    AND \"Quantity\" IS NULL AND \"UnitPrice\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingLines_AdditionalCost",
                table: "SpendingLines");

            migrationBuilder.DropColumn(
                name: "IsAdditionalCost",
                table: "SpendingLines");
        }
    }
}
