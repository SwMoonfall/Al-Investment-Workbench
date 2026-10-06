using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInvestmentWorkbench.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvestmentThesis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Theses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    InvestmentSummary = table.Column<string>(type: "TEXT", nullable: false),
                    WhyMarketMayBeWrong = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedHoldingPeriod = table.Column<string>(type: "TEXT", nullable: false),
                    BaseCaseNarrative = table.Column<string>(type: "TEXT", nullable: false),
                    BullCaseNarrative = table.Column<string>(type: "TEXT", nullable: false),
                    BearCaseNarrative = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedCatalysts = table.Column<string>(type: "TEXT", nullable: false),
                    KeyRisks = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReviewDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Theses", x => x.Id);
                    table.CheckConstraint("CK_Thesis_Lifecycle", "Status BETWEEN 0 AND 4 AND Version >= 1 AND (IsCurrent = 0 OR (Archived = 0 AND Status IN (1,2))) AND (Status <> 1 OR IsCurrent = 1) AND (Archived = 0 OR Status IN (3,4))");
                    table.ForeignKey(
                        name: "FK_Theses_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KillConditions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ThesisId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Metric = table.Column<string>(type: "TEXT", nullable: false),
                    WarningThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    TriggerThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    CurrentValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KillConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KillConditions_Theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "Theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ThesisAssumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ThesisId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Metric = table.Column<string>(type: "TEXT", nullable: false),
                    Baseline = table.Column<decimal>(type: "TEXT", nullable: true),
                    ExpectedValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    WarningThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    KillThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    CurrentValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThesisAssumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThesisAssumptions_Theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "Theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ThesisVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ThesisId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeReason = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThesisVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThesisVersions_Theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "Theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KillConditions_ThesisId",
                table: "KillConditions",
                column: "ThesisId");

            migrationBuilder.CreateIndex(
                name: "IX_Theses_SecurityId",
                table: "Theses",
                column: "SecurityId",
                unique: true,
                filter: "IsCurrent = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ThesisAssumptions_ThesisId",
                table: "ThesisAssumptions",
                column: "ThesisId");

            migrationBuilder.CreateIndex(
                name: "IX_ThesisVersions_ThesisId_Version",
                table: "ThesisVersions",
                columns: new[] { "ThesisId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KillConditions");

            migrationBuilder.DropTable(
                name: "ThesisAssumptions");

            migrationBuilder.DropTable(
                name: "ThesisVersions");

            migrationBuilder.DropTable(
                name: "Theses");
        }
    }
}
