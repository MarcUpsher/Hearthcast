using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetPaymentAnchorDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PaymentAnchorDate",
                table: "BudgetItems",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BudgetItems_PaymentAnchorDate",
                table: "BudgetItems",
                sql: "\"PaymentAnchorDate\" IS NULL OR (\"PaymentAnchorDate\" >= DATE '0001-01-02' AND \"PaymentAnchorDate\" <= DATE '9999-12-30')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BudgetItems_PaymentAnchorDate",
                table: "BudgetItems");

            migrationBuilder.DropColumn(
                name: "PaymentAnchorDate",
                table: "BudgetItems");
        }
    }
}
