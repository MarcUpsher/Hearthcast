using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancedAssetSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancedAssetSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancedAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetName = table.Column<string>(type: "text", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryName = table.Column<string>(type: "text", nullable: false),
                    FinancialType = table.Column<int>(type: "integer", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupName = table.Column<string>(type: "text", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    OriginalLoanAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    HasWithdrawalFacility = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInHousehold = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentValuation = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrentValuationValueOrigin = table.Column<int>(type: "integer", nullable: false),
                    CurrentValuationLastConfirmedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentValuationLastConfirmedPrecision = table.Column<int>(type: "integer", nullable: true),
                    CurrentValuationCarriedFromSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutstandingLoanBalance = table.Column<decimal>(type: "numeric", nullable: true),
                    OutstandingLoanBalanceValueOrigin = table.Column<int>(type: "integer", nullable: false),
                    OutstandingLoanBalanceLastConfirmedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OutstandingLoanBalanceLastConfirmedPrecision = table.Column<int>(type: "integer", nullable: true),
                    OutstandingLoanBalanceCarriedFromSnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    AvailableToWithdraw = table.Column<decimal>(type: "numeric", nullable: true),
                    AvailableToWithdrawValueOrigin = table.Column<int>(type: "integer", nullable: false),
                    AvailableToWithdrawLastConfirmedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AvailableToWithdrawLastConfirmedPrecision = table.Column<int>(type: "integer", nullable: true),
                    AvailableToWithdrawCarriedFromSnapshotId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancedAssetSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_FinancedAssetSnapshots_FinancedAssetId_CurrencyCode_Id", x => new { x.FinancedAssetId, x.CurrencyCode, x.Id });
                    table.UniqueConstraint("AK_FinancedAssetSnapshots_SnapshotId_Id", x => new { x.SnapshotId, x.Id });
                    table.CheckConstraint("CK_FinancedAssetSnapshots_AvailableToWithdraw_Amount", "\"AvailableToWithdraw\" >= 0 AND \"AvailableToWithdraw\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_AvailableToWithdraw_Source", "\"AvailableToWithdrawCarriedFromSnapshotId\" IS NULL OR \"AvailableToWithdrawCarriedFromSnapshotId\" <> \"Id\"");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_AvailableToWithdraw_State", "(\"AvailableToWithdraw\" IS NULL AND \"AvailableToWithdrawValueOrigin\" = 1 AND \"AvailableToWithdrawCarriedFromSnapshotId\" IS NULL\n    AND \"AvailableToWithdrawLastConfirmedDate\" IS NULL AND \"AvailableToWithdrawLastConfirmedPrecision\" IS NULL)\nOR (\"AvailableToWithdraw\" IS NOT NULL AND \"AvailableToWithdrawValueOrigin\" = 2 AND \"AvailableToWithdrawCarriedFromSnapshotId\" IS NULL\n    AND \"AvailableToWithdrawLastConfirmedDate\" IS NOT NULL AND \"AvailableToWithdrawLastConfirmedPrecision\" IS NOT NULL\n    AND \"AvailableToWithdrawLastConfirmedPrecision\" IN (1, 2))\nOR (\"AvailableToWithdraw\" IS NOT NULL AND \"AvailableToWithdrawValueOrigin\" = 3 AND \"AvailableToWithdrawCarriedFromSnapshotId\" IS NOT NULL\n    AND ((\"AvailableToWithdrawLastConfirmedDate\" IS NULL AND \"AvailableToWithdrawLastConfirmedPrecision\" IS NULL)\n        OR (\"AvailableToWithdrawLastConfirmedDate\" IS NOT NULL AND \"AvailableToWithdrawLastConfirmedPrecision\" IS NOT NULL\n            AND \"AvailableToWithdrawLastConfirmedPrecision\" IN (1, 2))))");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_Country", "\"CountryCode\" ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_CurrentValuation_Amount", "\"CurrentValuation\" >= 0 AND \"CurrentValuation\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_CurrentValuation_Source", "\"CurrentValuationCarriedFromSnapshotId\" IS NULL OR \"CurrentValuationCarriedFromSnapshotId\" <> \"Id\"");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_CurrentValuation_State", "(\"CurrentValuation\" IS NULL AND \"CurrentValuationValueOrigin\" = 1 AND \"CurrentValuationCarriedFromSnapshotId\" IS NULL\n    AND \"CurrentValuationLastConfirmedDate\" IS NULL AND \"CurrentValuationLastConfirmedPrecision\" IS NULL)\nOR (\"CurrentValuation\" IS NOT NULL AND \"CurrentValuationValueOrigin\" = 2 AND \"CurrentValuationCarriedFromSnapshotId\" IS NULL\n    AND \"CurrentValuationLastConfirmedDate\" IS NOT NULL AND \"CurrentValuationLastConfirmedPrecision\" IS NOT NULL\n    AND \"CurrentValuationLastConfirmedPrecision\" IN (1, 2))\nOR (\"CurrentValuation\" IS NOT NULL AND \"CurrentValuationValueOrigin\" = 3 AND \"CurrentValuationCarriedFromSnapshotId\" IS NOT NULL\n    AND ((\"CurrentValuationLastConfirmedDate\" IS NULL AND \"CurrentValuationLastConfirmedPrecision\" IS NULL)\n        OR (\"CurrentValuationLastConfirmedDate\" IS NOT NULL AND \"CurrentValuationLastConfirmedPrecision\" IS NOT NULL\n            AND \"CurrentValuationLastConfirmedPrecision\" IN (1, 2))))");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_FinancialType", "\"FinancialType\" = 1");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_Names", "length(trim(\"AssetName\")) > 0 AND length(trim(\"GroupName\")) > 0 AND length(trim(\"CategoryName\")) > 0");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_OriginalLoan", "\"OriginalLoanAmount\" >= 0 AND \"OriginalLoanAmount\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_OutstandingLoanBalance_Amount", "\"OutstandingLoanBalance\" >= 0 AND \"OutstandingLoanBalance\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_OutstandingLoanBalance_Source", "\"OutstandingLoanBalanceCarriedFromSnapshotId\" IS NULL OR \"OutstandingLoanBalanceCarriedFromSnapshotId\" <> \"Id\"");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_OutstandingLoanBalance_State", "(\"OutstandingLoanBalance\" IS NULL AND \"OutstandingLoanBalanceValueOrigin\" = 1 AND \"OutstandingLoanBalanceCarriedFromSnapshotId\" IS NULL\n    AND \"OutstandingLoanBalanceLastConfirmedDate\" IS NULL AND \"OutstandingLoanBalanceLastConfirmedPrecision\" IS NULL)\nOR (\"OutstandingLoanBalance\" IS NOT NULL AND \"OutstandingLoanBalanceValueOrigin\" = 2 AND \"OutstandingLoanBalanceCarriedFromSnapshotId\" IS NULL\n    AND \"OutstandingLoanBalanceLastConfirmedDate\" IS NOT NULL AND \"OutstandingLoanBalanceLastConfirmedPrecision\" IS NOT NULL\n    AND \"OutstandingLoanBalanceLastConfirmedPrecision\" IN (1, 2))\nOR (\"OutstandingLoanBalance\" IS NOT NULL AND \"OutstandingLoanBalanceValueOrigin\" = 3 AND \"OutstandingLoanBalanceCarriedFromSnapshotId\" IS NOT NULL\n    AND ((\"OutstandingLoanBalanceLastConfirmedDate\" IS NULL AND \"OutstandingLoanBalanceLastConfirmedPrecision\" IS NULL)\n        OR (\"OutstandingLoanBalanceLastConfirmedDate\" IS NOT NULL AND \"OutstandingLoanBalanceLastConfirmedPrecision\" IS NOT NULL\n            AND \"OutstandingLoanBalanceLastConfirmedPrecision\" IN (1, 2))))");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_Price", "\"PurchasePrice\" >= 0 AND \"PurchasePrice\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_FinancedAssetSnapshots_WithdrawalFacility", "\"HasWithdrawalFacility\" OR (\"AvailableToWithdraw\" IS NULL AND \"AvailableToWithdrawValueOrigin\" = 1)");
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshots_AvailableToWithdrawSource",
                        columns: x => new { x.FinancedAssetId, x.CurrencyCode, x.AvailableToWithdrawCarriedFromSnapshotId },
                        principalTable: "FinancedAssetSnapshots",
                        principalColumns: new[] { "FinancedAssetId", "CurrencyCode", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshots_Categories_HouseholdId_CategoryId",
                        columns: x => new { x.HouseholdId, x.CategoryId },
                        principalTable: "Categories",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshots_CurrentValuationSource",
                        columns: x => new { x.FinancedAssetId, x.CurrencyCode, x.CurrentValuationCarriedFromSnapshotId },
                        principalTable: "FinancedAssetSnapshots",
                        principalColumns: new[] { "FinancedAssetId", "CurrencyCode", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshots_FinancedAssets_HouseholdId_FinancedA~",
                        columns: x => new { x.HouseholdId, x.FinancedAssetId },
                        principalTable: "FinancedAssets",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshots_ItemGroups_HouseholdId_GroupId",
                        columns: x => new { x.HouseholdId, x.GroupId },
                        principalTable: "ItemGroups",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshots_OutstandingLoanBalanceSource",
                        columns: x => new { x.FinancedAssetId, x.CurrencyCode, x.OutstandingLoanBalanceCarriedFromSnapshotId },
                        principalTable: "FinancedAssetSnapshots",
                        principalColumns: new[] { "FinancedAssetId", "CurrencyCode", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshots_Snapshots_HouseholdId_SnapshotId",
                        columns: x => new { x.HouseholdId, x.SnapshotId },
                        principalTable: "Snapshots",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancedAssetSnapshotHouseholdAllocations",
                columns: table => new
                {
                    FinancedAssetSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareBasisPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancedAssetSnapshotHouseholdAllocations", x => new { x.FinancedAssetSnapshotId, x.SnapshotPersonId });
                    table.CheckConstraint("CK_FinancedAssetSnapshotHouseholdAllocations_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshotHouseholdAllocations_FinancedAssetSnap~",
                        columns: x => new { x.SnapshotId, x.FinancedAssetSnapshotId },
                        principalTable: "FinancedAssetSnapshots",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshotHouseholdAllocations_SnapshotPeople_Sn~",
                        columns: x => new { x.SnapshotId, x.SnapshotPersonId },
                        principalTable: "SnapshotPeople",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancedAssetSnapshotLoanHolders",
                columns: table => new
                {
                    FinancedAssetSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancedAssetSnapshotLoanHolders", x => new { x.FinancedAssetSnapshotId, x.SnapshotPersonId });
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshotLoanHolders_FinancedAssetSnapshots_Sna~",
                        columns: x => new { x.SnapshotId, x.FinancedAssetSnapshotId },
                        principalTable: "FinancedAssetSnapshots",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshotLoanHolders_SnapshotPeople_SnapshotId_~",
                        columns: x => new { x.SnapshotId, x.SnapshotPersonId },
                        principalTable: "SnapshotPeople",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancedAssetSnapshotOwners",
                columns: table => new
                {
                    FinancedAssetSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareBasisPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancedAssetSnapshotOwners", x => new { x.FinancedAssetSnapshotId, x.SnapshotPersonId });
                    table.CheckConstraint("CK_FinancedAssetSnapshotOwners_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshotOwners_FinancedAssetSnapshots_Snapshot~",
                        columns: x => new { x.SnapshotId, x.FinancedAssetSnapshotId },
                        principalTable: "FinancedAssetSnapshots",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetSnapshotOwners_SnapshotPeople_SnapshotId_Snaps~",
                        columns: x => new { x.SnapshotId, x.SnapshotPersonId },
                        principalTable: "SnapshotPeople",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshotHouseholdAllocations_SnapshotId_Financ~",
                table: "FinancedAssetSnapshotHouseholdAllocations",
                columns: new[] { "SnapshotId", "FinancedAssetSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshotHouseholdAllocations_SnapshotId_Snapsh~",
                table: "FinancedAssetSnapshotHouseholdAllocations",
                columns: new[] { "SnapshotId", "SnapshotPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshotLoanHolders_SnapshotId_FinancedAssetSn~",
                table: "FinancedAssetSnapshotLoanHolders",
                columns: new[] { "SnapshotId", "FinancedAssetSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshotLoanHolders_SnapshotId_SnapshotPersonId",
                table: "FinancedAssetSnapshotLoanHolders",
                columns: new[] { "SnapshotId", "SnapshotPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshotOwners_SnapshotId_FinancedAssetSnapsho~",
                table: "FinancedAssetSnapshotOwners",
                columns: new[] { "SnapshotId", "FinancedAssetSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshotOwners_SnapshotId_SnapshotPersonId",
                table: "FinancedAssetSnapshotOwners",
                columns: new[] { "SnapshotId", "SnapshotPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshots_AvailableToWithdrawSource",
                table: "FinancedAssetSnapshots",
                columns: new[] { "FinancedAssetId", "CurrencyCode", "AvailableToWithdrawCarriedFromSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshots_CurrentValuationSource",
                table: "FinancedAssetSnapshots",
                columns: new[] { "FinancedAssetId", "CurrencyCode", "CurrentValuationCarriedFromSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshots_HouseholdId_CategoryId",
                table: "FinancedAssetSnapshots",
                columns: new[] { "HouseholdId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshots_HouseholdId_FinancedAssetId",
                table: "FinancedAssetSnapshots",
                columns: new[] { "HouseholdId", "FinancedAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshots_HouseholdId_GroupId",
                table: "FinancedAssetSnapshots",
                columns: new[] { "HouseholdId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshots_HouseholdId_SnapshotId",
                table: "FinancedAssetSnapshots",
                columns: new[] { "HouseholdId", "SnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshots_OutstandingLoanBalanceSource",
                table: "FinancedAssetSnapshots",
                columns: new[] { "FinancedAssetId", "CurrencyCode", "OutstandingLoanBalanceCarriedFromSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetSnapshots_SnapshotId_FinancedAssetId",
                table: "FinancedAssetSnapshots",
                columns: new[] { "SnapshotId", "FinancedAssetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancedAssetSnapshotHouseholdAllocations");

            migrationBuilder.DropTable(
                name: "FinancedAssetSnapshotLoanHolders");

            migrationBuilder.DropTable(
                name: "FinancedAssetSnapshotOwners");

            migrationBuilder.DropTable(
                name: "FinancedAssetSnapshots");
        }
    }
}
