using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateInitialPostgresSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.Id);
                    table.CheckConstraint("CK_Households_Name_NotBlank", "length(trim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FinancialType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.UniqueConstraint("AK_Categories_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_Categories_FinancialType", "\"FinancialType\" IN (1, 2)");
                    table.CheckConstraint("CK_Categories_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.ForeignKey(
                        name: "FK_Categories_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    ParticipatesInHouseholdFinances = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                    table.UniqueConstraint("AK_People_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_People_DisplayName_NotBlank", "length(trim(\"DisplayName\")) > 0");
                    table.ForeignKey(
                        name: "FK_People_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdName = table.Column<string>(type: "text", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "ItemGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemGroups", x => x.Id);
                    table.UniqueConstraint("AK_ItemGroups_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_ItemGroups_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.ForeignKey(
                        name: "FK_ItemGroups_Categories_HouseholdId_CategoryId",
                        columns: x => new { x.HouseholdId, x.CategoryId },
                        principalTable: "Categories",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SnapshotPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    ParticipatesInHouseholdFinances = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotPeople", x => x.Id);
                    table.UniqueConstraint("AK_SnapshotPeople_SnapshotId_Id", x => new { x.SnapshotId, x.Id });
                    table.CheckConstraint("CK_SnapshotPeople_DisplayName_NotBlank", "length(trim(\"DisplayName\")) > 0");
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
                name: "TrackedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    HolderPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    IncludeInHousehold = table.Column<bool>(type: "boolean", nullable: false),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedItems", x => x.Id);
                    table.UniqueConstraint("AK_TrackedItems_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_TrackedItems_CountryCode", "\"CountryCode\" ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("CK_TrackedItems_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_TrackedItems_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.ForeignKey(
                        name: "FK_TrackedItems_ItemGroups_HouseholdId_GroupId",
                        columns: x => new { x.HouseholdId, x.GroupId },
                        principalTable: "ItemGroups",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackedItems_People_HouseholdId_HolderPersonId",
                        columns: x => new { x.HouseholdId, x.HolderPersonId },
                        principalTable: "People",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackedItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    HolderSnapshotPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryName = table.Column<string>(type: "text", nullable: false),
                    FinancialType = table.Column<int>(type: "integer", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupName = table.Column<string>(type: "text", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    IncludeInHousehold = table.Column<bool>(type: "boolean", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    ValueOrigin = table.Column<int>(type: "integer", nullable: false),
                    LastConfirmedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastConfirmedPrecision = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemSnapshots", x => x.Id);
                    table.UniqueConstraint("AK_ItemSnapshots_SnapshotId_Id", x => new { x.SnapshotId, x.Id });
                    table.CheckConstraint("CK_ItemSnapshots_Amount_Nonnegative", "\"Amount\" >= 0 AND \"Amount\" < 'Infinity'::numeric");
                    table.CheckConstraint("CK_ItemSnapshots_CountryCode", "\"CountryCode\" ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("CK_ItemSnapshots_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_ItemSnapshots_FinancialType", "\"FinancialType\" IN (1, 2)");
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
                        name: "FK_ItemSnapshots_SnapshotPeople_SnapshotId_HolderSnapshotPerso~",
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
                name: "TrackedItemHouseholdAllocations",
                columns: table => new
                {
                    TrackedItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareBasisPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedItemHouseholdAllocations", x => new { x.TrackedItemId, x.PersonId });
                    table.CheckConstraint("CK_TrackedItemHouseholdAllocations_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_TrackedItemHouseholdAllocations_People_HouseholdId_PersonId",
                        columns: x => new { x.HouseholdId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackedItemHouseholdAllocations_TrackedItems_HouseholdId_Tr~",
                        columns: x => new { x.HouseholdId, x.TrackedItemId },
                        principalTable: "TrackedItems",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemSnapshotHouseholdAllocations",
                columns: table => new
                {
                    ItemSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotPersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareBasisPoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemSnapshotHouseholdAllocations", x => new { x.ItemSnapshotId, x.SnapshotPersonId });
                    table.CheckConstraint("CK_ItemSnapshotHouseholdAllocations_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000");
                    table.ForeignKey(
                        name: "FK_ItemSnapshotHouseholdAllocations_ItemSnapshots_SnapshotId_I~",
                        columns: x => new { x.SnapshotId, x.ItemSnapshotId },
                        principalTable: "ItemSnapshots",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemSnapshotHouseholdAllocations_SnapshotPeople_SnapshotId_~",
                        columns: x => new { x.SnapshotId, x.SnapshotPersonId },
                        principalTable: "SnapshotPeople",
                        principalColumns: new[] { "SnapshotId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemGroups_HouseholdId_CategoryId",
                table: "ItemGroups",
                columns: new[] { "HouseholdId", "CategoryId" });

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

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItemHouseholdAllocations_HouseholdId_PersonId",
                table: "TrackedItemHouseholdAllocations",
                columns: new[] { "HouseholdId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItemHouseholdAllocations_HouseholdId_TrackedItemId",
                table: "TrackedItemHouseholdAllocations",
                columns: new[] { "HouseholdId", "TrackedItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItems_HouseholdId_GroupId",
                table: "TrackedItems",
                columns: new[] { "HouseholdId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedItems_HouseholdId_HolderPersonId",
                table: "TrackedItems",
                columns: new[] { "HouseholdId", "HolderPersonId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemSnapshotHouseholdAllocations");

            migrationBuilder.DropTable(
                name: "TrackedItemHouseholdAllocations");

            migrationBuilder.DropTable(
                name: "ItemSnapshots");

            migrationBuilder.DropTable(
                name: "SnapshotPeople");

            migrationBuilder.DropTable(
                name: "TrackedItems");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "ItemGroups");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Households");
        }
    }
}
