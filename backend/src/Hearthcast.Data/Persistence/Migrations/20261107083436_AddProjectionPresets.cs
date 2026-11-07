using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectionPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectionPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    DurationYears = table.Column<int>(type: "integer", nullable: false),
                    MonthlyInvestmentContribution = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualInvestmentReturnPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    MonthlySavingsContribution = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualSavingsInterestPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualInflationPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    ShowInflationAdjustedValues = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionPresets", x => x.Id);
                    table.UniqueConstraint("AK_ProjectionPresets_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_ProjectionPresets_AnnualInflationPercent", "\"AnnualInflationPercent\" > -100 AND \"AnnualInflationPercent\" <= 100");
                    table.CheckConstraint("CK_ProjectionPresets_AnnualInvestmentReturnPercent", "\"AnnualInvestmentReturnPercent\" > -100 AND \"AnnualInvestmentReturnPercent\" <= 100");
                    table.CheckConstraint("CK_ProjectionPresets_AnnualSavingsInterestPercent", "\"AnnualSavingsInterestPercent\" > -100 AND \"AnnualSavingsInterestPercent\" <= 100");
                    table.CheckConstraint("CK_ProjectionPresets_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_ProjectionPresets_DurationYears", "\"DurationYears\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_ProjectionPresets_MonthlyInvestmentContribution", "\"MonthlyInvestmentContribution\" >= 0 AND \"MonthlyInvestmentContribution\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_ProjectionPresets_MonthlySavingsContribution", "\"MonthlySavingsContribution\" >= 0 AND \"MonthlySavingsContribution\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_ProjectionPresets_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.ForeignKey(
                        name: "FK_ProjectionPresets_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionPresetFinancedAssets",
                columns: table => new
                {
                    ProjectionPresetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancedAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthlyLoanPaymentInAssetCurrency = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualLoanInterestPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualAssetAppreciationPercent = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionPresetFinancedAssets", x => new { x.ProjectionPresetId, x.FinancedAssetId });
                    table.CheckConstraint("CK_ProjectionPresetFinancedAssets_AnnualAssetAppreciationPerce~", "\"AnnualAssetAppreciationPercent\" > -100 AND \"AnnualAssetAppreciationPercent\" <= 100");
                    table.CheckConstraint("CK_ProjectionPresetFinancedAssets_AnnualLoanInterestPercent", "\"AnnualLoanInterestPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_ProjectionPresetFinancedAssets_MonthlyLoanPaymentInAssetCur~", "\"MonthlyLoanPaymentInAssetCurrency\" >= 0 AND \"MonthlyLoanPaymentInAssetCurrency\" < 'Infinity'::numeric");
                    table.ForeignKey(
                        name: "FK_ProjectionPresetFinancedAssets_FinancedAssets_HouseholdId_F~",
                        columns: x => new { x.HouseholdId, x.FinancedAssetId },
                        principalTable: "FinancedAssets",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectionPresetFinancedAssets_ProjectionPresets_HouseholdI~",
                        columns: x => new { x.HouseholdId, x.ProjectionPresetId },
                        principalTable: "ProjectionPresets",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionPresetGroups",
                columns: table => new
                {
                    ProjectionPresetId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bucket = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionPresetGroups", x => new { x.ProjectionPresetId, x.GroupId });
                    table.CheckConstraint("CK_ProjectionPresetGroups_Bucket", "\"Bucket\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_ProjectionPresetGroups_ItemGroups_HouseholdId_GroupId",
                        columns: x => new { x.HouseholdId, x.GroupId },
                        principalTable: "ItemGroups",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectionPresetGroups_ProjectionPresets_HouseholdId_Projec~",
                        columns: x => new { x.HouseholdId, x.ProjectionPresetId },
                        principalTable: "ProjectionPresets",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionPresetFinancedAssets_HouseholdId_FinancedAssetId",
                table: "ProjectionPresetFinancedAssets",
                columns: new[] { "HouseholdId", "FinancedAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionPresetFinancedAssets_HouseholdId_ProjectionPreset~",
                table: "ProjectionPresetFinancedAssets",
                columns: new[] { "HouseholdId", "ProjectionPresetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionPresetGroups_HouseholdId_GroupId",
                table: "ProjectionPresetGroups",
                columns: new[] { "HouseholdId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionPresetGroups_HouseholdId_ProjectionPresetId",
                table: "ProjectionPresetGroups",
                columns: new[] { "HouseholdId", "ProjectionPresetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionPresets_HouseholdId_Name",
                table: "ProjectionPresets",
                columns: new[] { "HouseholdId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectionPresetFinancedAssets");

            migrationBuilder.DropTable(
                name: "ProjectionPresetGroups");

            migrationBuilder.DropTable(
                name: "ProjectionPresets");
        }
    }
}
