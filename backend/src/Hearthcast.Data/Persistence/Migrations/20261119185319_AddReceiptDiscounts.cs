using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpendingDiscounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpendingRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseCategoryName = table.Column<string>(type: "text", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CurrencyMinorUnits = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpendingDiscounts", x => x.Id);
                    table.CheckConstraint("CK_SpendingDiscounts_Amount", "\"Amount\" > 0 AND \"Amount\" < 1000000000000 AND \"Amount\" = round(\"Amount\", \"CurrencyMinorUnits\")");
                    table.CheckConstraint("CK_SpendingDiscounts_CategoryName", "length(\"ExpenseCategoryName\") > 0 AND \"ExpenseCategoryName\" = btrim(\"ExpenseCategoryName\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");
                    table.CheckConstraint("CK_SpendingDiscounts_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$' AND \"CurrencyMinorUnits\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_SpendingDiscounts_Description", "\"Description\" IS NULL OR (length(\"Description\") > 0 AND \"Description\" = btrim(\"Description\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingDiscounts_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_SpendingDiscounts_Position", "\"Position\" > 0");
                    table.ForeignKey(
                        name: "FK_SpendingDiscounts_ExpenseCategories_HouseholdId_ExpenseCate~",
                        columns: x => new { x.HouseholdId, x.ExpenseCategoryId },
                        principalTable: "ExpenseCategories",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpendingDiscounts_SpendingRecords_HouseholdId_SpendingRecor~",
                        columns: x => new { x.HouseholdId, x.SpendingRecordId, x.CurrencyCode, x.CurrencyMinorUnits },
                        principalTable: "SpendingRecords",
                        principalColumns: new[] { "HouseholdId", "Id", "CurrencyCode", "CurrencyMinorUnits" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingDiscounts_HouseholdId_ExpenseCategoryId",
                table: "SpendingDiscounts",
                columns: new[] { "HouseholdId", "ExpenseCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingDiscounts_HouseholdId_SpendingRecordId_CurrencyCode~",
                table: "SpendingDiscounts",
                columns: new[] { "HouseholdId", "SpendingRecordId", "CurrencyCode", "CurrencyMinorUnits" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingDiscounts_SpendingRecordId_Position",
                table: "SpendingDiscounts",
                columns: new[] { "SpendingRecordId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpendingDiscounts");
        }
    }
}
