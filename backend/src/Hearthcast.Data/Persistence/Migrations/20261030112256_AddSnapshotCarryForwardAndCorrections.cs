using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotCarryForwardAndCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemSnapshots_ValueState",
                table: "ItemSnapshots");

            migrationBuilder.AddColumn<Guid>(
                name: "Revision",
                table: "Snapshots",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "CarriedFromItemSnapshotId",
                table: "ItemSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ItemSnapshots_TrackedItemId_CurrencyCode_Id",
                table: "ItemSnapshots",
                columns: new[] { "TrackedItemId", "CurrencyCode", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshots_TrackedItemId_CurrencyCode_CarriedFromItemSna~",
                table: "ItemSnapshots",
                columns: new[] { "TrackedItemId", "CurrencyCode", "CarriedFromItemSnapshotId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemSnapshots_NotSelfSource",
                table: "ItemSnapshots",
                sql: "\"CarriedFromItemSnapshotId\" IS NULL OR \"CarriedFromItemSnapshotId\" <> \"Id\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemSnapshots_ValueState",
                table: "ItemSnapshots",
                sql: "(\"Amount\" IS NULL AND \"ValueOrigin\" = 1 AND \"CarriedFromItemSnapshotId\" IS NULL\n    AND \"LastConfirmedDate\" IS NULL AND \"LastConfirmedPrecision\" IS NULL)\nOR (\"Amount\" IS NOT NULL AND \"ValueOrigin\" = 2 AND \"CarriedFromItemSnapshotId\" IS NULL\n    AND \"LastConfirmedDate\" IS NOT NULL AND \"LastConfirmedPrecision\" IS NOT NULL\n    AND \"LastConfirmedPrecision\" IN (1, 2))\nOR (\"Amount\" IS NOT NULL AND \"ValueOrigin\" = 3 AND \"CarriedFromItemSnapshotId\" IS NOT NULL\n    AND ((\"LastConfirmedDate\" IS NULL AND \"LastConfirmedPrecision\" IS NULL)\n        OR (\"LastConfirmedDate\" IS NOT NULL AND \"LastConfirmedPrecision\" IS NOT NULL\n            AND \"LastConfirmedPrecision\" IN (1, 2))))");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemSnapshots_ItemSnapshots_TrackedItemId_CurrencyCode_Carr~",
                table: "ItemSnapshots",
                columns: new[] { "TrackedItemId", "CurrencyCode", "CarriedFromItemSnapshotId" },
                principalTable: "ItemSnapshots",
                principalColumns: new[] { "TrackedItemId", "CurrencyCode", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemSnapshots_ItemSnapshots_TrackedItemId_CurrencyCode_Carr~",
                table: "ItemSnapshots");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ItemSnapshots_TrackedItemId_CurrencyCode_Id",
                table: "ItemSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ItemSnapshots_TrackedItemId_CurrencyCode_CarriedFromItemSna~",
                table: "ItemSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemSnapshots_NotSelfSource",
                table: "ItemSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemSnapshots_ValueState",
                table: "ItemSnapshots");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "CarriedFromItemSnapshotId",
                table: "ItemSnapshots");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemSnapshots_ValueState",
                table: "ItemSnapshots",
                sql: "(\"Amount\" IS NULL AND \"ValueOrigin\" = 1\n    AND \"LastConfirmedDate\" IS NULL AND \"LastConfirmedPrecision\" IS NULL)\nOR (\"Amount\" IS NOT NULL AND \"ValueOrigin\" = 2\n    AND \"LastConfirmedDate\" IS NOT NULL AND \"LastConfirmedPrecision\" IS NOT NULL\n    AND \"LastConfirmedPrecision\" IN (1, 2))");
        }
    }
}
