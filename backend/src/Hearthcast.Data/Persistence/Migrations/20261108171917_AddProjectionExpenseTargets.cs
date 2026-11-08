using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectionExpenseTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualExpenses",
                table: "ProjectionPresets",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualWithdrawalRatePercent",
                table: "ProjectionPresets",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpenseCurrencyCode",
                table: "ProjectionPresets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeSavingsInExpenseTarget",
                table: "ProjectionPresets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectionPresets_ExpenseTarget",
                table: "ProjectionPresets",
                sql: "(\"AnnualExpenses\" IS NULL AND \"ExpenseCurrencyCode\" IS NULL AND \"AnnualWithdrawalRatePercent\" IS NULL AND NOT \"IncludeSavingsInExpenseTarget\")\nOR (\"AnnualExpenses\" IS NOT NULL AND \"ExpenseCurrencyCode\" IS NOT NULL AND \"AnnualWithdrawalRatePercent\" IS NOT NULL\n    AND \"AnnualExpenses\" > 0 AND \"AnnualExpenses\" < 'Infinity'::numeric\n    AND \"ExpenseCurrencyCode\" ~ '^[A-Z]{3}$'\n    AND \"AnnualWithdrawalRatePercent\" > 0 AND \"AnnualWithdrawalRatePercent\" <= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectionPresets_ExpenseTarget",
                table: "ProjectionPresets");

            migrationBuilder.DropColumn(
                name: "AnnualExpenses",
                table: "ProjectionPresets");

            migrationBuilder.DropColumn(
                name: "AnnualWithdrawalRatePercent",
                table: "ProjectionPresets");

            migrationBuilder.DropColumn(
                name: "ExpenseCurrencyCode",
                table: "ProjectionPresets");

            migrationBuilder.DropColumn(
                name: "IncludeSavingsInExpenseTarget",
                table: "ProjectionPresets");
        }
    }
}
