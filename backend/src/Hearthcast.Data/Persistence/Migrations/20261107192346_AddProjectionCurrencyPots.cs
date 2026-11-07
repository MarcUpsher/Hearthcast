using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectionCurrencyPots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectionPresets_AnnualInvestmentReturnPercent",
                table: "ProjectionPresets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectionPresets_AnnualSavingsInterestPercent",
                table: "ProjectionPresets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectionPresets_MonthlyInvestmentContribution",
                table: "ProjectionPresets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectionPresets_MonthlySavingsContribution",
                table: "ProjectionPresets");





            migrationBuilder.AddColumn<decimal>(
                name: "ExtraMonthlyLoanPaymentInAssetCurrency",
                table: "ProjectionPresetFinancedAssets",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "InvestmentCurrencyAfterPayoff",
                table: "ProjectionPresetFinancedAssets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ProjectionPresetCurrencies",
                columns: table => new
                {
                    ProjectionPresetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    MonthlyInvestmentContribution = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualInvestmentReturnPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    MonthlySavingsContribution = table.Column<decimal>(type: "numeric", nullable: false),
                    AnnualSavingsInterestPercent = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionPresetCurrencies", x => new { x.ProjectionPresetId, x.CurrencyCode });
                    table.CheckConstraint("CK_ProjectionPresetCurrencies_AnnualInvestmentReturnPercent", "\"AnnualInvestmentReturnPercent\" > -100 AND \"AnnualInvestmentReturnPercent\" <= 100");
                    table.CheckConstraint("CK_ProjectionPresetCurrencies_AnnualSavingsInterestPercent", "\"AnnualSavingsInterestPercent\" > -100 AND \"AnnualSavingsInterestPercent\" <= 100");
                    table.CheckConstraint("CK_ProjectionPresetCurrencies_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_ProjectionPresetCurrencies_MonthlyInvestmentContribution", "\"MonthlyInvestmentContribution\" >= 0 AND \"MonthlyInvestmentContribution\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_ProjectionPresetCurrencies_MonthlySavingsContribution", "\"MonthlySavingsContribution\" >= 0 AND \"MonthlySavingsContribution\" < 'Infinity'::numeric");
                    table.ForeignKey(
                        name: "FK_ProjectionPresetCurrencies_ProjectionPresets_ProjectionPres~",
                        column: x => x.ProjectionPresetId,
                        principalTable: "ProjectionPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve each existing single-currency preset before removing its old scalar columns.
            migrationBuilder.Sql("""
                INSERT INTO "ProjectionPresetCurrencies" ("ProjectionPresetId", "CurrencyCode", "MonthlyInvestmentContribution",
                    "AnnualInvestmentReturnPercent", "MonthlySavingsContribution", "AnnualSavingsInterestPercent")
                SELECT "Id", "CurrencyCode", "MonthlyInvestmentContribution", "AnnualInvestmentReturnPercent",
                    "MonthlySavingsContribution", "AnnualSavingsInterestPercent" FROM "ProjectionPresets";
                -- Keep selected foreign holdings usable with the previous shared nominal returns.
                INSERT INTO "ProjectionPresetCurrencies" ("ProjectionPresetId", "CurrencyCode", "MonthlyInvestmentContribution",
                    "AnnualInvestmentReturnPercent", "MonthlySavingsContribution", "AnnualSavingsInterestPercent")
                SELECT DISTINCT p."Id", native."CurrencyCode", 0, p."AnnualInvestmentReturnPercent", 0, p."AnnualSavingsInterestPercent"
                FROM "ProjectionPresets" p JOIN "ProjectionPresetGroups" g ON g."ProjectionPresetId" = p."Id"
                JOIN LATERAL (
                    SELECT i."CurrencyCode" FROM "TrackedItems" i
                    WHERE i."HouseholdId" = p."HouseholdId" AND i."GroupId" = g."GroupId" AND i."IncludeInHousehold"
                    UNION
                    SELECT i."CurrencyCode" FROM "ItemSnapshots" i
                    WHERE i."HouseholdId" = p."HouseholdId" AND i."GroupId" = g."GroupId" AND i."IncludeInHousehold" AND i."FinancialType" = 1
                ) native ON TRUE
                ON CONFLICT ("ProjectionPresetId", "CurrencyCode") DO NOTHING;
                UPDATE "ProjectionPresetFinancedAssets" AS a SET "InvestmentCurrencyAfterPayoff" = p."CurrencyCode"
                FROM "ProjectionPresets" AS p WHERE a."ProjectionPresetId" = p."Id";
                """);

            migrationBuilder.DropColumn(
                name: "AnnualInvestmentReturnPercent",
                table: "ProjectionPresets");
            migrationBuilder.DropColumn(
                name: "AnnualSavingsInterestPercent",
                table: "ProjectionPresets");
            migrationBuilder.DropColumn(
                name: "MonthlyInvestmentContribution",
                table: "ProjectionPresets");
            migrationBuilder.DropColumn(
                name: "MonthlySavingsContribution",
                table: "ProjectionPresets");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionPresetFinancedAssets_ProjectionPresetId_Investmen~",
                table: "ProjectionPresetFinancedAssets",
                columns: new[] { "ProjectionPresetId", "InvestmentCurrencyAfterPayoff" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectionPresetFinancedAssets_ExtraPayment",
                table: "ProjectionPresetFinancedAssets",
                sql: "\"ExtraMonthlyLoanPaymentInAssetCurrency\" >= 0 AND \"ExtraMonthlyLoanPaymentInAssetCurrency\" < 'Infinity'::numeric");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectionPresetFinancedAssets_InvestmentCurrency",
                table: "ProjectionPresetFinancedAssets",
                sql: "\"InvestmentCurrencyAfterPayoff\" ~ '^[A-Z]{3}$'");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectionPresetFinancedAssets_ProjectionPresetCurrencies_P~",
                table: "ProjectionPresetFinancedAssets",
                columns: new[] { "ProjectionPresetId", "InvestmentCurrencyAfterPayoff" },
                principalTable: "ProjectionPresetCurrencies",
                principalColumns: new[] { "ProjectionPresetId", "CurrencyCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A downgrade must not silently discard extra payments, pots or destination choices.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ProjectionPresetCurrencies" c JOIN "ProjectionPresets" p
                        ON c."ProjectionPresetId" = p."Id" WHERE c."CurrencyCode" <> p."CurrencyCode")
                        OR EXISTS (SELECT 1 FROM "ProjectionPresetFinancedAssets" a JOIN "ProjectionPresets" p
                        ON a."ProjectionPresetId" = p."Id" WHERE a."ExtraMonthlyLoanPaymentInAssetCurrency" <> 0
                        OR a."InvestmentCurrencyAfterPayoff" <> p."CurrencyCode") THEN
                        RAISE EXCEPTION 'Cannot downgrade presets with multi-currency or extra-payment settings without losing data';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectionPresetFinancedAssets_ProjectionPresetCurrencies_P~",
                table: "ProjectionPresetFinancedAssets");


            migrationBuilder.DropIndex(
                name: "IX_ProjectionPresetFinancedAssets_ProjectionPresetId_Investmen~",
                table: "ProjectionPresetFinancedAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectionPresetFinancedAssets_ExtraPayment",
                table: "ProjectionPresetFinancedAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectionPresetFinancedAssets_InvestmentCurrency",
                table: "ProjectionPresetFinancedAssets");

            migrationBuilder.DropColumn(
                name: "ExtraMonthlyLoanPaymentInAssetCurrency",
                table: "ProjectionPresetFinancedAssets");

            migrationBuilder.DropColumn(
                name: "InvestmentCurrencyAfterPayoff",
                table: "ProjectionPresetFinancedAssets");

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualInvestmentReturnPercent",
                table: "ProjectionPresets",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualSavingsInterestPercent",
                table: "ProjectionPresets",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyInvestmentContribution",
                table: "ProjectionPresets",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlySavingsContribution",
                table: "ProjectionPresets",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "ProjectionPresets" p SET "MonthlyInvestmentContribution" = c."MonthlyInvestmentContribution",
                    "AnnualInvestmentReturnPercent" = c."AnnualInvestmentReturnPercent",
                    "MonthlySavingsContribution" = c."MonthlySavingsContribution",
                    "AnnualSavingsInterestPercent" = c."AnnualSavingsInterestPercent"
                FROM "ProjectionPresetCurrencies" c WHERE c."ProjectionPresetId" = p."Id" AND c."CurrencyCode" = p."CurrencyCode";
                """);

            migrationBuilder.DropTable(
                name: "ProjectionPresetCurrencies");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectionPresets_AnnualInvestmentReturnPercent",
                table: "ProjectionPresets",
                sql: "\"AnnualInvestmentReturnPercent\" > -100 AND \"AnnualInvestmentReturnPercent\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectionPresets_AnnualSavingsInterestPercent",
                table: "ProjectionPresets",
                sql: "\"AnnualSavingsInterestPercent\" > -100 AND \"AnnualSavingsInterestPercent\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectionPresets_MonthlyInvestmentContribution",
                table: "ProjectionPresets",
                sql: "\"MonthlyInvestmentContribution\" >= 0 AND \"MonthlyInvestmentContribution\" < 'Infinity'::numeric");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectionPresets_MonthlySavingsContribution",
                table: "ProjectionPresets",
                sql: "\"MonthlySavingsContribution\" >= 0 AND \"MonthlySavingsContribution\" < 'Infinity'::numeric");
        }
    }
}
