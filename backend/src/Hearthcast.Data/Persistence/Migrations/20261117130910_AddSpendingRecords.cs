using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpendingRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceSpendingLineId",
                table: "SpendingItemMerchantPrices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpendingRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SpendingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantName = table.Column<string>(type: "text", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CurrencyMinorUnits = table.Column<int>(type: "integer", nullable: false),
                    ExternalReference = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ReceiptTotal = table.Column<decimal>(type: "numeric", nullable: true),
                    OriginalSpendingRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalSpendingRecordKind = table.Column<int>(type: "integer", nullable: true),
                    CreationRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpendingRecords", x => x.Id);
                    table.UniqueConstraint("AK_SpendingRecords_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.UniqueConstraint("AK_SpendingRecords_HouseholdId_Id_CurrencyCode_CurrencyMinorUn~", x => new { x.HouseholdId, x.Id, x.CurrencyCode, x.CurrencyMinorUnits });
                    table.UniqueConstraint("AK_SpendingRecords_HouseholdId_Id_Kind", x => new { x.HouseholdId, x.Id, x.Kind });
                    table.CheckConstraint("CK_SpendingRecords_CreationHash", "\"CreationRequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_SpendingRecords_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$' AND \"CurrencyMinorUnits\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_SpendingRecords_Date", "\"SpendingDate\" > DATE '0001-01-01' AND \"SpendingDate\" <= DATE '9999-12-31'");
                    table.CheckConstraint("CK_SpendingRecords_ExternalReference", "\"ExternalReference\" IS NULL OR (length(\"ExternalReference\") > 0 AND \"ExternalReference\" = btrim(\"ExternalReference\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingRecords_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_SpendingRecords_Kind", "\"Kind\" IN (1, 2)");
                    table.CheckConstraint("CK_SpendingRecords_MerchantName", "length(\"MerchantName\") > 0 AND \"MerchantName\" = btrim(\"MerchantName\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");
                    table.CheckConstraint("CK_SpendingRecords_Notes", "\"Notes\" IS NULL OR (length(\"Notes\") > 0 AND \"Notes\" = btrim(\"Notes\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingRecords_OriginalPayment", "(\"OriginalSpendingRecordId\" IS NULL AND \"OriginalSpendingRecordKind\" IS NULL)\nOR (\"Kind\" = 2 AND \"OriginalSpendingRecordId\" IS NOT NULL AND \"OriginalSpendingRecordKind\" IS NOT NULL\n    AND \"OriginalSpendingRecordKind\" = 1 AND \"OriginalSpendingRecordId\" <> \"Id\")");
                    table.CheckConstraint("CK_SpendingRecords_ReceiptTotal", "\"ReceiptTotal\" IS NULL OR (\"ReceiptTotal\" >= 0 AND \"ReceiptTotal\" < 1000000000000 AND \"ReceiptTotal\" = round(\"ReceiptTotal\", \"CurrencyMinorUnits\"))");
                    table.CheckConstraint("CK_SpendingRecords_Revision", "\"Revision\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_SpendingRecords_Timestamps", "isfinite(\"CreatedAtUtc\") AND isfinite(\"UpdatedAtUtc\") AND \"UpdatedAtUtc\" >= \"CreatedAtUtc\" AND (\"VoidedAtUtc\" IS NULL OR (isfinite(\"VoidedAtUtc\") AND \"VoidedAtUtc\" >= \"CreatedAtUtc\" AND \"VoidedAtUtc\" <= \"UpdatedAtUtc\"))");
                    table.ForeignKey(
                        name: "FK_SpendingRecords_Merchants_HouseholdId_MerchantId",
                        columns: x => new { x.HouseholdId, x.MerchantId },
                        principalTable: "Merchants",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpendingRecords_SpendingRecords_HouseholdId_OriginalSpendin~",
                        columns: x => new { x.HouseholdId, x.OriginalSpendingRecordId, x.OriginalSpendingRecordKind },
                        principalTable: "SpendingRecords",
                        principalColumns: new[] { "HouseholdId", "Id", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpendingLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpendingRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CurrencyMinorUnits = table.Column<int>(type: "integer", nullable: false),
                    SpendingItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ItemName = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseCategoryName = table.Column<string>(type: "text", nullable: false),
                    UnitKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UnitLabel = table.Column<string>(type: "text", nullable: true),
                    ReceiptDescription = table.Column<string>(type: "text", nullable: true),
                    MerchantItemReference = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    NetAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PromotionStatus = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpendingLines", x => x.Id);
                    table.UniqueConstraint("AK_SpendingLines_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_SpendingLines_CategoryName", "length(\"ExpenseCategoryName\") > 0 AND \"ExpenseCategoryName\" = btrim(\"ExpenseCategoryName\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");
                    table.CheckConstraint("CK_SpendingLines_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$' AND \"CurrencyMinorUnits\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_SpendingLines_Description", "\"Description\" IS NULL OR (length(\"Description\") > 0 AND \"Description\" = btrim(\"Description\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingLines_Item", "(\"SpendingItemId\" IS NULL AND \"ItemCode\" IS NULL AND \"ItemName\" IS NULL)\nOR (\"SpendingItemId\" IS NOT NULL AND \"ItemCode\" IS NOT NULL AND \"ItemName\" IS NOT NULL\n    AND \"UnitKey\" IS NOT NULL AND \"UnitLabel\" IS NOT NULL)");
                    table.CheckConstraint("CK_SpendingLines_ItemCode", "\"ItemCode\" IS NULL OR \"ItemCode\" ~ '^[A-Z0-9][A-Z0-9._-]*$'");
                    table.CheckConstraint("CK_SpendingLines_ItemName", "\"ItemName\" IS NULL OR (length(\"ItemName\") > 0 AND \"ItemName\" = btrim(\"ItemName\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingLines_MerchantItemReference", "\"MerchantItemReference\" IS NULL OR (length(\"MerchantItemReference\") > 0 AND \"MerchantItemReference\" = btrim(\"MerchantItemReference\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingLines_NetAmount", "\"NetAmount\" >= 0 AND \"NetAmount\" < 1000000000000 AND \"NetAmount\" = round(\"NetAmount\", \"CurrencyMinorUnits\")");
                    table.CheckConstraint("CK_SpendingLines_Notes", "\"Notes\" IS NULL OR (length(\"Notes\") > 0 AND \"Notes\" = btrim(\"Notes\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingLines_Position", "\"Position\" > 0");
                    table.CheckConstraint("CK_SpendingLines_Promotion", "\"PromotionStatus\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_SpendingLines_Quantity", "\"Quantity\" IS NULL OR (\"Quantity\" > 0 AND \"Quantity\" < 1000000 AND \"Quantity\" = round(\"Quantity\", 6))");
                    table.CheckConstraint("CK_SpendingLines_ReceiptDescription", "\"ReceiptDescription\" IS NULL OR (length(\"ReceiptDescription\") > 0 AND \"ReceiptDescription\" = btrim(\"ReceiptDescription\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingLines_UnitKey", "\"UnitKey\" IS NULL OR \"UnitKey\" ~ '^[a-z0-9][a-z0-9._-]*$'");
                    table.CheckConstraint("CK_SpendingLines_UnitLabel", "\"UnitLabel\" IS NULL OR (length(\"UnitLabel\") > 0 AND \"UnitLabel\" = btrim(\"UnitLabel\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_SpendingLines_UnitPrice", "\"UnitPrice\" IS NULL OR (\"UnitPrice\" >= 0 AND \"UnitPrice\" < 1000000000 AND \"UnitPrice\" = round(\"UnitPrice\", 6))");
                    table.CheckConstraint("CK_SpendingLines_Units", "((\"Quantity\" IS NULL AND \"UnitPrice\" IS NULL)\n    OR (\"Quantity\" IS NOT NULL AND \"UnitPrice\" IS NOT NULL AND \"UnitKey\" IS NOT NULL))\nAND ((\"UnitKey\" IS NULL AND \"UnitLabel\" IS NULL) OR (\"UnitKey\" IS NOT NULL AND \"UnitLabel\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_SpendingLines_ExpenseCategories_HouseholdId_ExpenseCategory~",
                        columns: x => new { x.HouseholdId, x.ExpenseCategoryId },
                        principalTable: "ExpenseCategories",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpendingLines_SpendingItems_HouseholdId_SpendingItemId_Unit~",
                        columns: x => new { x.HouseholdId, x.SpendingItemId, x.UnitKey },
                        principalTable: "SpendingItems",
                        principalColumns: new[] { "HouseholdId", "Id", "UnitKey" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpendingLines_SpendingRecords_HouseholdId_SpendingRecordId_~",
                        columns: x => new { x.HouseholdId, x.SpendingRecordId, x.CurrencyCode, x.CurrencyMinorUnits },
                        principalTable: "SpendingRecords",
                        principalColumns: new[] { "HouseholdId", "Id", "CurrencyCode", "CurrencyMinorUnits" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingItemMerchantPrices_HouseholdId_SourceSpendingLineId",
                table: "SpendingItemMerchantPrices",
                columns: new[] { "HouseholdId", "SourceSpendingLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingLines_HouseholdId_ExpenseCategoryId",
                table: "SpendingLines",
                columns: new[] { "HouseholdId", "ExpenseCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingLines_HouseholdId_SpendingItemId_UnitKey",
                table: "SpendingLines",
                columns: new[] { "HouseholdId", "SpendingItemId", "UnitKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingLines_HouseholdId_SpendingRecordId_CurrencyCode_Cur~",
                table: "SpendingLines",
                columns: new[] { "HouseholdId", "SpendingRecordId", "CurrencyCode", "CurrencyMinorUnits" });

            migrationBuilder.CreateIndex(
                name: "UX_SpendingLines_RecordPosition",
                table: "SpendingLines",
                columns: new[] { "SpendingRecordId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpendingRecords_HouseholdId_MerchantId",
                table: "SpendingRecords",
                columns: new[] { "HouseholdId", "MerchantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingRecords_HouseholdId_OriginalSpendingRecordId_Origin~",
                table: "SpendingRecords",
                columns: new[] { "HouseholdId", "OriginalSpendingRecordId", "OriginalSpendingRecordKind" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingRecords_HouseholdId_SpendingDate",
                table: "SpendingRecords",
                columns: new[] { "HouseholdId", "SpendingDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_SpendingItemMerchantPrices_SpendingLines_HouseholdId_Source~",
                table: "SpendingItemMerchantPrices",
                columns: new[] { "HouseholdId", "SourceSpendingLineId" },
                principalTable: "SpendingLines",
                principalColumns: new[] { "HouseholdId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SpendingItemMerchantPrices_SpendingLines_HouseholdId_Source~",
                table: "SpendingItemMerchantPrices");

            migrationBuilder.DropTable(
                name: "SpendingLines");

            migrationBuilder.DropTable(
                name: "SpendingRecords");

            migrationBuilder.DropIndex(
                name: "IX_SpendingItemMerchantPrices_HouseholdId_SourceSpendingLineId",
                table: "SpendingItemMerchantPrices");

            migrationBuilder.DropColumn(
                name: "SourceSpendingLineId",
                table: "SpendingItemMerchantPrices");
        }
    }
}
