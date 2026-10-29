using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateOrdinarySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseholdName = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DatePrecision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                    table.UniqueConstraint("AK_Snapshots_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_Snapshots_DatePrecision", "\"DatePrecision\" IN (1, 2)");
                    table.CheckConstraint("CK_Snapshots_HouseholdName_NotBlank", "length(trim(\"HouseholdName\")) > 0");
                    table.ForeignKey(
                        name: "FK_Snapshots_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SnapshotPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    ParticipatesInHouseholdFinances = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotPeople", x => x.Id);
                    table.UniqueConstraint("AK_SnapshotPeople_SnapshotId_Id", x => new { x.SnapshotId, x.Id });
                    table.CheckConstraint("CK_SnapshotPeople_DisplayName_NotBlank", "length(trim(\"DisplayName\")) > 0");
                    table.CheckConstraint("CK_SnapshotPeople_Participation_Boolean", "\"ParticipatesInHouseholdFinances\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_SnapshotPeople_People_HouseholdId_PersonId",
                        columns: x => new { x.HouseholdId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SnapshotPeople_Snapshots_HouseholdId_SnapshotId",
                        columns: x => new { x.HouseholdId, x.SnapshotId },
                        principalTable: "Snapshots",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackedItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HolderSnapshotPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", nullable: false),
                    FinancialType = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupName = table.Column<string>(type: "TEXT", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    IncludeInHousehold = table.Column<bool>(type: "INTEGER", nullable: false),
                    Amount = table.Column<string>(type: "TEXT", nullable: true),
                    ValueOrigin = table.Column<int>(type: "INTEGER", nullable: false),
                    LastConfirmedDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastConfirmedPrecision = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_ItemSnapshots_SnapshotId_Id", x => new { x.SnapshotId, x.Id });
                    table.CheckConstraint("CK_ItemSnapshots_CountryCode", "\"CountryCode\" GLOB '[A-Z][A-Z]'");
                    table.CheckConstraint("CK_ItemSnapshots_CurrencyCode", "\"CurrencyCode\" GLOB '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("CK_ItemSnapshots_FinancialType", "\"FinancialType\" IN (1, 2)");
                    table.CheckConstraint("CK_ItemSnapshots_Inclusion_Boolean", "\"IncludeInHousehold\" IN (0, 1)");
                    table.CheckConstraint("CK_ItemSnapshots_Names_NotBlank", "length(trim(\"ItemName\")) > 0 AND length(trim(\"GroupName\")) > 0 AND length(trim(\"CategoryName\")) > 0");
                    table.CheckConstraint("CK_ItemSnapshots_ValueState", "(\"Amount\" IS NULL AND \"ValueOrigin\" = 1\n    AND \"LastConfirmedDate\" IS NULL AND \"LastConfirmedPrecision\" IS NULL)\nOR (\"Amount\" IS NOT NULL AND \"ValueOrigin\" = 2\n    AND \"LastConfirmedDate\" IS NOT NULL AND \"LastConfirmedPrecision\" IS NOT NULL\n    AND \"LastConfirmedPrecision\" IN (1, 2))");
                    table.ForeignKey(
                        name: "FK_ItemSnapshots_Categories_HouseholdId_CategoryId",
                        columns: x => new { x.HouseholdId, x.CategoryId },
                        principalTable: "Categories",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemSnapshots_ItemGroups_HouseholdId_GroupId",
                        columns: x => new { x.HouseholdId, x.GroupId },
                        principalTable: "ItemGroups",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemSnapshots_SnapshotPeople_SnapshotId_HolderSnapshotPersonId",
                        columns: x => new { x.SnapshotId, x.HolderSnapshotPersonId },
                        principalTable: "SnapshotPeople",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemSnapshots_Snapshots_HouseholdId_SnapshotId",
                        columns: x => new { x.HouseholdId, x.SnapshotId },
                        principalTable: "Snapshots",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemSnapshots_TrackedItems_HouseholdId_TrackedItemId",
                        columns: x => new { x.HouseholdId, x.TrackedItemId },
                        principalTable: "TrackedItems",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemSnapshotHouseholdAllocations",
                columns: table => new
                {
                    ItemSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShareBasisPoints = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemSnapshotHouseholdAllocations", x => new { x.ItemSnapshotId, x.SnapshotPersonId });
                    table.CheckConstraint("CK_ItemSnapshotHouseholdAllocations_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_ItemSnapshotHouseholdAllocations_ItemSnapshots_SnapshotId_ItemSnapshotId",
                        columns: x => new { x.SnapshotId, x.ItemSnapshotId },
                        principalTable: "ItemSnapshots",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemSnapshotHouseholdAllocations_SnapshotPeople_SnapshotId_SnapshotPersonId",
                        columns: x => new { x.SnapshotId, x.SnapshotPersonId },
                        principalTable: "SnapshotPeople",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshotHouseholdAllocations_SnapshotId_ItemSnapshotId",
                table: "ItemSnapshotHouseholdAllocations",
                columns: new[] { "SnapshotId", "ItemSnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshotHouseholdAllocations_SnapshotId_SnapshotPersonId",
                table: "ItemSnapshotHouseholdAllocations",
                columns: new[] { "SnapshotId", "SnapshotPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshots_HouseholdId_CategoryId",
                table: "ItemSnapshots",
                columns: new[] { "HouseholdId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshots_HouseholdId_GroupId",
                table: "ItemSnapshots",
                columns: new[] { "HouseholdId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshots_HouseholdId_SnapshotId",
                table: "ItemSnapshots",
                columns: new[] { "HouseholdId", "SnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshots_HouseholdId_TrackedItemId",
                table: "ItemSnapshots",
                columns: new[] { "HouseholdId", "TrackedItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshots_SnapshotId_HolderSnapshotPersonId",
                table: "ItemSnapshots",
                columns: new[] { "SnapshotId", "HolderSnapshotPersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemSnapshots_SnapshotId_TrackedItemId",
                table: "ItemSnapshots",
                columns: new[] { "SnapshotId", "TrackedItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotPeople_HouseholdId_PersonId",
                table: "SnapshotPeople",
                columns: new[] { "HouseholdId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotPeople_HouseholdId_SnapshotId",
                table: "SnapshotPeople",
                columns: new[] { "HouseholdId", "SnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotPeople_SnapshotId_PersonId",
                table: "SnapshotPeople",
                columns: new[] { "SnapshotId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_HouseholdId_SnapshotDate",
                table: "Snapshots",
                columns: new[] { "HouseholdId", "SnapshotDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemSnapshotHouseholdAllocations");

            migrationBuilder.DropTable(
                name: "ItemSnapshots");

            migrationBuilder.DropTable(
                name: "SnapshotPeople");

            migrationBuilder.DropTable(
                name: "Snapshots");
        }
    }
}
