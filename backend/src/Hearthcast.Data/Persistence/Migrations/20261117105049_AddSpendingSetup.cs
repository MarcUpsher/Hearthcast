using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpendingSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                    table.UniqueConstraint("AK_ExpenseCategories_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_ExpenseCategories_Name", "\"Name\" ~ '[^[:space:]]' AND \"Name\" = btrim(\"Name\")");
                    table.ForeignKey(
                        name: "FK_ExpenseCategories_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Merchants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Merchants", x => x.Id);
                    table.UniqueConstraint("AK_Merchants_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_Merchants_Name", "\"Name\" ~ '[^[:space:]]' AND \"Name\" = btrim(\"Name\")");
                    table.ForeignKey(
                        name: "FK_Merchants_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpendingItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UnitKey = table.Column<string>(type: "text", nullable: false),
                    UnitLabel = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpendingItems", x => x.Id);
                    table.UniqueConstraint("AK_SpendingItems_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.UniqueConstraint("AK_SpendingItems_HouseholdId_Id_UnitKey", x => new { x.HouseholdId, x.Id, x.UnitKey });
                    table.CheckConstraint("CK_SpendingItems_Code", "\"Code\" ~ '^[A-Z0-9][A-Z0-9._-]*$'");
                    table.CheckConstraint("CK_SpendingItems_Name", "\"Name\" ~ '[^[:space:]]' AND \"Name\" = btrim(\"Name\")");
                    table.CheckConstraint("CK_SpendingItems_Notes", "\"Notes\" IS NULL OR (\"Notes\" ~ '[^[:space:]]' AND \"Notes\" = btrim(\"Notes\"))");
                    table.CheckConstraint("CK_SpendingItems_UnitKey", "\"UnitKey\" ~ '^[a-z0-9][a-z0-9._-]*$'");
                    table.CheckConstraint("CK_SpendingItems_UnitLabel", "\"UnitLabel\" ~ '[^[:space:]]' AND \"UnitLabel\" = btrim(\"UnitLabel\")");
                    table.ForeignKey(
                        name: "FK_SpendingItems_ExpenseCategories_HouseholdId_ExpenseCategory~",
                        columns: x => new { x.HouseholdId, x.ExpenseCategoryId },
                        principalTable: "ExpenseCategories",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpendingItemMerchantPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpendingItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    UnitKey = table.Column<string>(type: "text", nullable: false),
                    UnitLabel = table.Column<string>(type: "text", nullable: false),
                    UsualUnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpendingItemMerchantPrices", x => x.Id);
                    table.UniqueConstraint("AK_SpendingItemMerchantPrices_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_SpendingItemMerchantPrices_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_SpendingItemMerchantPrices_Revision", "\"Revision\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_SpendingItemMerchantPrices_UnitKey", "\"UnitKey\" ~ '^[a-z0-9][a-z0-9._-]*$'");
                    table.CheckConstraint("CK_SpendingItemMerchantPrices_UnitLabel", "\"UnitLabel\" ~ '[^[:space:]]' AND \"UnitLabel\" = btrim(\"UnitLabel\")");
                    table.CheckConstraint("CK_SpendingItemMerchantPrices_UpdatedAtUtc", "isfinite(\"UpdatedAtUtc\")");
                    table.CheckConstraint("CK_SpendingItemMerchantPrices_UsualUnitPrice", "\"UsualUnitPrice\" >= 0 AND \"UsualUnitPrice\" < 1000000000 AND \"UsualUnitPrice\" = round(\"UsualUnitPrice\", 6)");
                    table.ForeignKey(
                        name: "FK_SpendingItemMerchantPrices_Merchants_HouseholdId_MerchantId",
                        columns: x => new { x.HouseholdId, x.MerchantId },
                        principalTable: "Merchants",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpendingItemMerchantPrices_SpendingItems_HouseholdId_Spendi~",
                        columns: x => new { x.HouseholdId, x.SpendingItemId, x.UnitKey },
                        principalTable: "SpendingItems",
                        principalColumns: new[] { "HouseholdId", "Id", "UnitKey" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingItemMerchantPrices_HouseholdId_MerchantId",
                table: "SpendingItemMerchantPrices",
                columns: new[] { "HouseholdId", "MerchantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingItemMerchantPrices_HouseholdId_SpendingItemId_Merch~",
                table: "SpendingItemMerchantPrices",
                columns: new[] { "HouseholdId", "SpendingItemId", "MerchantId", "CurrencyCode", "UnitKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpendingItemMerchantPrices_HouseholdId_SpendingItemId_UnitK~",
                table: "SpendingItemMerchantPrices",
                columns: new[] { "HouseholdId", "SpendingItemId", "UnitKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SpendingItems_HouseholdId_Code",
                table: "SpendingItems",
                columns: new[] { "HouseholdId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpendingItems_HouseholdId_ExpenseCategoryId",
                table: "SpendingItems",
                columns: new[] { "HouseholdId", "ExpenseCategoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpendingItemMerchantPrices");

            migrationBuilder.DropTable(
                name: "Merchants");

            migrationBuilder.DropTable(
                name: "SpendingItems");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");
        }
    }
}
