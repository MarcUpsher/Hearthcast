using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetAllowances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "BudgetItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "BudgetEntries",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "UX_BudgetItems_ActiveAllowance",
                table: "BudgetItems",
                columns: new[] { "HouseholdId", "ExpenseCategoryId", "CurrencyCode" },
                unique: true,
                filter: "\"Kind\" = 2 AND NOT \"IsRetired\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BudgetItems_Allowance",
                table: "BudgetItems",
                sql: "\"Kind\" <> 2 OR (\"MerchantId\" IS NULL AND \"PaymentAnchorDate\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BudgetItems_Kind",
                table: "BudgetItems",
                sql: "\"Kind\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_BudgetEntries_Allowance",
                table: "BudgetEntries",
                columns: new[] { "BudgetSnapshotId", "ExpenseCategoryId", "CurrencyCode" },
                unique: true,
                filter: "\"Kind\" = 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BudgetEntries_Allowance",
                table: "BudgetEntries",
                sql: "\"Kind\" <> 2 OR (\"MerchantId\" IS NULL AND \"MerchantName\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BudgetEntries_Kind",
                table: "BudgetEntries",
                sql: "\"Kind\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_BudgetItems_ActiveAllowance",
                table: "BudgetItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BudgetItems_Allowance",
                table: "BudgetItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BudgetItems_Kind",
                table: "BudgetItems");

            migrationBuilder.DropIndex(
                name: "UX_BudgetEntries_Allowance",
                table: "BudgetEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BudgetEntries_Allowance",
                table: "BudgetEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BudgetEntries_Kind",
                table: "BudgetEntries");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BudgetItems");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BudgetEntries");

        }
    }
}
