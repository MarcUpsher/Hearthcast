using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateBaseCurrency",
                table: "Snapshots",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateLookupDate",
                table: "Snapshots",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SnapshotExchangeRates",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BaseCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(36,18)", precision: 36, scale: 18, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotExchangeRates", x => new { x.SnapshotId, x.QuoteCurrency });
                    table.CheckConstraint("CK_SnapshotExchangeRates_DifferentCurrencies", "\"BaseCurrency\" <> \"QuoteCurrency\"");
                    table.CheckConstraint("CK_SnapshotExchangeRates_Positive", "\"Rate\" > 0");
                    table.ForeignKey(
                        name: "FK_SnapshotExchangeRates_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SnapshotExchangeRates");

            migrationBuilder.DropColumn(
                name: "ExchangeRateBaseCurrency",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "ExchangeRateLookupDate",
                table: "Snapshots");
        }
    }
}
