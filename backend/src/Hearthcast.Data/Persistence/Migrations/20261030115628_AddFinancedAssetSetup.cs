using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancedAssetSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    OriginalLoanAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    HasWithdrawalFacility = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeInHousehold = table.Column<bool>(type: "boolean", nullable: false),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancedAssets", x => x.Id);
                    table.UniqueConstraint("AK_FinancedAssets_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_FinancedAssets_CountryCode", "\"CountryCode\" ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("CK_FinancedAssets_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_FinancedAssets_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.CheckConstraint("CK_FinancedAssets_OriginalLoanAmount", "\"OriginalLoanAmount\" >= 0 AND \"OriginalLoanAmount\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_FinancedAssets_PurchasePrice", "\"PurchasePrice\" >= 0 AND \"PurchasePrice\" < 'Infinity'::numeric");
                    table.ForeignKey(
                        name: "FK_FinancedAssets_ItemGroups_HouseholdId_GroupId",
                        columns: x => new { x.HouseholdId, x.GroupId },
                        principalTable: "ItemGroups",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancedAssetHouseholdAllocations",
                columns: table => new
                {
                    FinancedAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareBasisPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancedAssetHouseholdAllocations", x => new { x.FinancedAssetId, x.PersonId });
                    table.CheckConstraint("CK_FinancedAssetHouseholdAllocations_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_FinancedAssetHouseholdAllocations_FinancedAssets_HouseholdI~",
                        columns: x => new { x.HouseholdId, x.FinancedAssetId },
                        principalTable: "FinancedAssets",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetHouseholdAllocations_People_HouseholdId_Person~",
                        columns: x => new { x.HouseholdId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancedAssetLoanHolders",
                columns: table => new
                {
                    FinancedAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancedAssetLoanHolders", x => new { x.FinancedAssetId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_FinancedAssetLoanHolders_FinancedAssets_HouseholdId_Finance~",
                        columns: x => new { x.HouseholdId, x.FinancedAssetId },
                        principalTable: "FinancedAssets",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetLoanHolders_People_HouseholdId_PersonId",
                        columns: x => new { x.HouseholdId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancedAssetOwners",
                columns: table => new
                {
                    FinancedAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareBasisPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancedAssetOwners", x => new { x.FinancedAssetId, x.PersonId });
                    table.CheckConstraint("CK_FinancedAssetOwners_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_FinancedAssetOwners_FinancedAssets_HouseholdId_FinancedAsse~",
                        columns: x => new { x.HouseholdId, x.FinancedAssetId },
                        principalTable: "FinancedAssets",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancedAssetOwners_People_HouseholdId_PersonId",
                        columns: x => new { x.HouseholdId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetHouseholdAllocations_HouseholdId_FinancedAsset~",
                table: "FinancedAssetHouseholdAllocations",
                columns: new[] { "HouseholdId", "FinancedAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetHouseholdAllocations_HouseholdId_PersonId",
                table: "FinancedAssetHouseholdAllocations",
                columns: new[] { "HouseholdId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetLoanHolders_HouseholdId_FinancedAssetId",
                table: "FinancedAssetLoanHolders",
                columns: new[] { "HouseholdId", "FinancedAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetLoanHolders_HouseholdId_PersonId",
                table: "FinancedAssetLoanHolders",
                columns: new[] { "HouseholdId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetOwners_HouseholdId_FinancedAssetId",
                table: "FinancedAssetOwners",
                columns: new[] { "HouseholdId", "FinancedAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssetOwners_HouseholdId_PersonId",
                table: "FinancedAssetOwners",
                columns: new[] { "HouseholdId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancedAssets_HouseholdId_GroupId",
                table: "FinancedAssets",
                columns: new[] { "HouseholdId", "GroupId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancedAssetHouseholdAllocations");

            migrationBuilder.DropTable(
                name: "FinancedAssetLoanHolders");

            migrationBuilder.DropTable(
                name: "FinancedAssetOwners");

            migrationBuilder.DropTable(
                name: "FinancedAssets");
        }
    }
}
