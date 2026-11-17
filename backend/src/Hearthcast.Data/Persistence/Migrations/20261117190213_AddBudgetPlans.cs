using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    IntervalCount = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetItems", x => x.Id);
                    table.UniqueConstraint("AK_BudgetItems_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_BudgetItems_Code", "\"Code\" ~ '^[A-Z0-9][A-Z0-9._-]*$'");
                    table.CheckConstraint("CK_BudgetItems_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_BudgetItems_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BudgetItems_Name", "length(\"Name\") > 0 AND \"Name\" = btrim(\"Name\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");
                    table.CheckConstraint("CK_BudgetItems_Notes", "\"Notes\" IS NULL OR (length(\"Notes\") > 0 AND \"Notes\" = btrim(\"Notes\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_BudgetItems_Recurrence", "\"Frequency\" IN (1, 2, 3) AND \"IntervalCount\" > 0");
                    table.ForeignKey(
                        name: "FK_BudgetItems_ExpenseCategories_HouseholdId_ExpenseCategoryId",
                        columns: x => new { x.HouseholdId, x.ExpenseCategoryId },
                        principalTable: "ExpenseCategories",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetItems_Merchants_HouseholdId_MerchantId",
                        columns: x => new { x.HouseholdId, x.MerchantId },
                        principalTable: "Merchants",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CopiedFromBudgetSnapshotId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_BudgetSnapshots_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_BudgetSnapshots_CreationHash", "\"CreationRequestHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_BudgetSnapshots_EffectiveFrom", "\"EffectiveFrom\" > DATE '0001-01-01' AND \"EffectiveFrom\" < DATE '9999-12-31'");
                    table.CheckConstraint("CK_BudgetSnapshots_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BudgetSnapshots_Notes", "\"Notes\" IS NULL OR (length(\"Notes\") > 0 AND \"Notes\" = btrim(\"Notes\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_BudgetSnapshots_Revision", "\"Revision\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BudgetSnapshots_Source", "\"CopiedFromBudgetSnapshotId\" IS NULL OR \"CopiedFromBudgetSnapshotId\" <> \"Id\"");
                    table.CheckConstraint("CK_BudgetSnapshots_Timestamps", "isfinite(\"CreatedAtUtc\") AND isfinite(\"UpdatedAtUtc\") AND \"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_BudgetSnapshots_BudgetSnapshots_HouseholdId_CopiedFromBudge~",
                        columns: x => new { x.HouseholdId, x.CopiedFromBudgetSnapshotId },
                        principalTable: "BudgetSnapshots",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetSnapshots_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseCategoryName = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: true),
                    MerchantName = table.Column<string>(type: "text", nullable: true),
                    PlannedAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    IntervalCount = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ValueOrigin = table.Column<int>(type: "integer", nullable: false),
                    LastConfirmedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CarriedFromBudgetEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetEntries", x => x.Id);
                    table.UniqueConstraint("AK_BudgetEntries_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_BudgetEntries_Amount", "\"PlannedAmount\" IS NULL OR (\"PlannedAmount\" >= 0 AND \"PlannedAmount\" < 1000000000000 AND \"PlannedAmount\" = round(\"PlannedAmount\", 6))");
                    table.CheckConstraint("CK_BudgetEntries_ConfirmationDate", "\"LastConfirmedDate\" IS NULL OR (\"LastConfirmedDate\" > DATE '0001-01-01' AND \"LastConfirmedDate\" < DATE '9999-12-31')");
                    table.CheckConstraint("CK_BudgetEntries_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_BudgetEntries_ExpenseCategoryName", "length(\"ExpenseCategoryName\") > 0 AND \"ExpenseCategoryName\" = btrim(\"ExpenseCategoryName\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");
                    table.CheckConstraint("CK_BudgetEntries_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BudgetEntries_ItemCode", "\"ItemCode\" ~ '^[A-Z0-9][A-Z0-9._-]*$'");
                    table.CheckConstraint("CK_BudgetEntries_ItemName", "length(\"ItemName\") > 0 AND \"ItemName\" = btrim(\"ItemName\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')");
                    table.CheckConstraint("CK_BudgetEntries_Merchant", "(\"MerchantId\" IS NULL AND \"MerchantName\" IS NULL) OR (\"MerchantId\" IS NOT NULL AND \"MerchantName\" IS NOT NULL)");
                    table.CheckConstraint("CK_BudgetEntries_MerchantName", "\"MerchantName\" IS NULL OR (length(\"MerchantName\") > 0 AND \"MerchantName\" = btrim(\"MerchantName\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_BudgetEntries_Notes", "\"Notes\" IS NULL OR (length(\"Notes\") > 0 AND \"Notes\" = btrim(\"Notes\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000'))");
                    table.CheckConstraint("CK_BudgetEntries_Provenance", "(\"ValueOrigin\" = 1 AND \"PlannedAmount\" IS NULL AND \"LastConfirmedDate\" IS NULL AND \"CarriedFromBudgetEntryId\" IS NULL)\nOR (\"ValueOrigin\" = 2 AND \"PlannedAmount\" IS NOT NULL AND \"LastConfirmedDate\" IS NOT NULL AND \"CarriedFromBudgetEntryId\" IS NULL)\nOR (\"ValueOrigin\" = 3 AND \"PlannedAmount\" IS NOT NULL AND \"LastConfirmedDate\" IS NOT NULL)");
                    table.CheckConstraint("CK_BudgetEntries_Recurrence", "\"Frequency\" IN (1, 2, 3) AND \"IntervalCount\" > 0");
                    table.CheckConstraint("CK_BudgetEntries_Source", "\"CarriedFromBudgetEntryId\" IS NULL OR \"CarriedFromBudgetEntryId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_BudgetEntries_BudgetEntries_HouseholdId_CarriedFromBudgetEn~",
                        columns: x => new { x.HouseholdId, x.CarriedFromBudgetEntryId },
                        principalTable: "BudgetEntries",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetEntries_BudgetItems_HouseholdId_BudgetItemId",
                        columns: x => new { x.HouseholdId, x.BudgetItemId },
                        principalTable: "BudgetItems",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetEntries_BudgetSnapshots_HouseholdId_BudgetSnapshotId",
                        columns: x => new { x.HouseholdId, x.BudgetSnapshotId },
                        principalTable: "BudgetSnapshots",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetEntries_ExpenseCategories_HouseholdId_ExpenseCategory~",
                        columns: x => new { x.HouseholdId, x.ExpenseCategoryId },
                        principalTable: "ExpenseCategories",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetEntries_Merchants_HouseholdId_MerchantId",
                        columns: x => new { x.HouseholdId, x.MerchantId },
                        principalTable: "Merchants",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEntries_BudgetSnapshotId_BudgetItemId",
                table: "BudgetEntries",
                columns: new[] { "BudgetSnapshotId", "BudgetItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEntries_HouseholdId_BudgetItemId",
                table: "BudgetEntries",
                columns: new[] { "HouseholdId", "BudgetItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEntries_HouseholdId_BudgetSnapshotId",
                table: "BudgetEntries",
                columns: new[] { "HouseholdId", "BudgetSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEntries_HouseholdId_CarriedFromBudgetEntryId",
                table: "BudgetEntries",
                columns: new[] { "HouseholdId", "CarriedFromBudgetEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEntries_HouseholdId_ExpenseCategoryId",
                table: "BudgetEntries",
                columns: new[] { "HouseholdId", "ExpenseCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetEntries_HouseholdId_MerchantId",
                table: "BudgetEntries",
                columns: new[] { "HouseholdId", "MerchantId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetItems_HouseholdId_Code",
                table: "BudgetItems",
                columns: new[] { "HouseholdId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetItems_HouseholdId_ExpenseCategoryId",
                table: "BudgetItems",
                columns: new[] { "HouseholdId", "ExpenseCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetItems_HouseholdId_MerchantId",
                table: "BudgetItems",
                columns: new[] { "HouseholdId", "MerchantId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetSnapshots_HouseholdId_CopiedFromBudgetSnapshotId",
                table: "BudgetSnapshots",
                columns: new[] { "HouseholdId", "CopiedFromBudgetSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetSnapshots_HouseholdId_EffectiveFrom",
                table: "BudgetSnapshots",
                columns: new[] { "HouseholdId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetEntries");

            migrationBuilder.DropTable(
                name: "BudgetItems");

            migrationBuilder.DropTable(
                name: "BudgetSnapshots");
        }
    }
}
