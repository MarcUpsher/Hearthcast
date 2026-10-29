using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hearthcast.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateTrackedItemsAndHouseholdAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HolderPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: false),
                    IncludeInHousehold = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRetired = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedItems", x => x.Id);
                    table.UniqueConstraint("AK_TrackedItems_HouseholdId_Id", x => new { x.HouseholdId, x.Id });
                    table.CheckConstraint("CK_TrackedItems_CountryCode", "\"CountryCode\" GLOB '[A-Z][A-Z]'");
                    table.CheckConstraint("CK_TrackedItems_CurrencyCode", "\"CurrencyCode\" GLOB '[A-Z][A-Z][A-Z]'");
                    table.CheckConstraint("CK_TrackedItems_Inclusion_Boolean", "\"IncludeInHousehold\" IN (0, 1)");
                    table.CheckConstraint("CK_TrackedItems_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.CheckConstraint("CK_TrackedItems_Retirement_Boolean", "\"IsRetired\" IN (0, 1)");
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
                name: "TrackedItemHouseholdAllocations",
                columns: table => new
                {
                    TrackedItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShareBasisPoints = table.Column<int>(type: "INTEGER", nullable: false)
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
                        name: "FK_TrackedItemHouseholdAllocations_TrackedItems_HouseholdId_TrackedItemId",
                        columns: x => new { x.HouseholdId, x.TrackedItemId },
                        principalTable: "TrackedItems",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "TrackedItemHouseholdAllocations");

            migrationBuilder.DropTable(
                name: "TrackedItems");
        }
    }
}
