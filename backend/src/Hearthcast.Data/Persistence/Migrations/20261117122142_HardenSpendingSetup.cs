using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenSpendingSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Never shorten existing identities silently when tightening indexed columns.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "SpendingItems" WHERE length("Code") > 64 OR length("UnitKey") > 32)
                        OR EXISTS (SELECT 1 FROM "SpendingItemMerchantPrices" WHERE length("UnitKey") > 32) THEN
                        RAISE EXCEPTION 'Cannot tighten spending identity limits: shorten codes to 64 and unit keys to 32 characters before migrating.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingItems_Name",
                table: "SpendingItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingItems_Notes",
                table: "SpendingItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingItems_UnitLabel",
                table: "SpendingItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingItemMerchantPrices_UnitLabel",
                table: "SpendingItemMerchantPrices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Merchants_Name",
                table: "Merchants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ExpenseCategories_Name",
                table: "ExpenseCategories");

            migrationBuilder.RenameIndex(
                name: "IX_SpendingItemMerchantPrices_HouseholdId_SpendingItemId_Merch~",
                table: "SpendingItemMerchantPrices",
                newName: "UX_SpendingPrices_ItemMerchantCurrencyUnit");

            migrationBuilder.AlterColumn<string>(
                name: "UnitKey",
                table: "SpendingItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "SpendingItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "UnitKey",
                table: "SpendingItemMerchantPrices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingItems_Name",
                table: "SpendingItems",
                sql: "length(\"Name\") > 0 AND \"Name\" = btrim(\"Name\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingItems_Notes",
                table: "SpendingItems",
                sql: "\"Notes\" IS NULL OR (length(\"Notes\") > 0 AND \"Notes\" = btrim(\"Notes\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingItems_UnitLabel",
                table: "SpendingItems",
                sql: "length(\"UnitLabel\") > 0 AND \"UnitLabel\" = btrim(\"UnitLabel\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingItemMerchantPrices_UnitLabel",
                table: "SpendingItemMerchantPrices",
                sql: "length(\"UnitLabel\") > 0 AND \"UnitLabel\" = btrim(\"UnitLabel\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Merchants_Name",
                table: "Merchants",
                sql: "length(\"Name\") > 0 AND \"Name\" = btrim(\"Name\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExpenseCategories_Name",
                table: "ExpenseCategories",
                sql: "length(\"Name\") > 0 AND \"Name\" = btrim(\"Name\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingItems_Name",
                table: "SpendingItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingItems_Notes",
                table: "SpendingItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingItems_UnitLabel",
                table: "SpendingItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SpendingItemMerchantPrices_UnitLabel",
                table: "SpendingItemMerchantPrices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Merchants_Name",
                table: "Merchants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ExpenseCategories_Name",
                table: "ExpenseCategories");

            migrationBuilder.RenameIndex(
                name: "UX_SpendingPrices_ItemMerchantCurrencyUnit",
                table: "SpendingItemMerchantPrices",
                newName: "IX_SpendingItemMerchantPrices_HouseholdId_SpendingItemId_Merch~");

            migrationBuilder.AlterColumn<string>(
                name: "UnitKey",
                table: "SpendingItems",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "SpendingItems",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "UnitKey",
                table: "SpendingItemMerchantPrices",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingItems_Name",
                table: "SpendingItems",
                sql: "\"Name\" ~ '[^[:space:]]' AND \"Name\" = btrim(\"Name\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingItems_Notes",
                table: "SpendingItems",
                sql: "\"Notes\" IS NULL OR (\"Notes\" ~ '[^[:space:]]' AND \"Notes\" = btrim(\"Notes\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingItems_UnitLabel",
                table: "SpendingItems",
                sql: "\"UnitLabel\" ~ '[^[:space:]]' AND \"UnitLabel\" = btrim(\"UnitLabel\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SpendingItemMerchantPrices_UnitLabel",
                table: "SpendingItemMerchantPrices",
                sql: "\"UnitLabel\" ~ '[^[:space:]]' AND \"UnitLabel\" = btrim(\"UnitLabel\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Merchants_Name",
                table: "Merchants",
                sql: "\"Name\" ~ '[^[:space:]]' AND \"Name\" = btrim(\"Name\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExpenseCategories_Name",
                table: "ExpenseCategories",
                sql: "\"Name\" ~ '[^[:space:]]' AND \"Name\" = btrim(\"Name\")");
        }
    }
}
