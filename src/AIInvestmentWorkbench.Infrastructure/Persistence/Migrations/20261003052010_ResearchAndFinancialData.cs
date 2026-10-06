using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInvestmentWorkbench.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResearchAndFinancialData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyResearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Overview = table.Column<string>(type: "TEXT", nullable: false),
                    BusinessModel = table.Column<string>(type: "TEXT", nullable: false),
                    Industry = table.Column<string>(type: "TEXT", nullable: false),
                    Competition = table.Column<string>(type: "TEXT", nullable: false),
                    Management = table.Column<string>(type: "TEXT", nullable: false),
                    GrowthDrivers = table.Column<string>(type: "TEXT", nullable: false),
                    Catalysts = table.Column<string>(type: "TEXT", nullable: false),
                    Risks = table.Column<string>(type: "TEXT", nullable: false),
                    AccountingNotes = table.Column<string>(type: "TEXT", nullable: false),
                    UserNotes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyResearches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyResearches_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResearchScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    Weight = table.Column<decimal>(type: "TEXT", nullable: false),
                    AIScore = table.Column<decimal>(type: "TEXT", nullable: true),
                    UserScore = table.Column<decimal>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchScores", x => x.Id);
                    table.CheckConstraint("CK_Score_Ranges", "CAST(Weight AS REAL) BETWEEN 0 AND 100 AND (UserScore IS NULL OR CAST(UserScore AS REAL) BETWEEN 0 AND 100) AND (AIScore IS NULL OR CAST(AIScore AS REAL) BETWEEN 0 AND 100)");
                    table.ForeignKey(
                        name: "FK_ResearchScores_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResearchSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    LocalFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ReliabilityLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ExtractedText = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchSources", x => x.Id);
                    table.CheckConstraint("CK_Source_Reliability", "ReliabilityLevel BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_ResearchSources_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Period = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    PeriodType = table.Column<int>(type: "INTEGER", nullable: false),
                    MetricType = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialMetrics_ResearchSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "ResearchSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialMetrics_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyResearches_SecurityId",
                table: "CompanyResearches",
                column: "SecurityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMetrics_SecurityId_PeriodType_Period_MetricType_Currency",
                table: "FinancialMetrics",
                columns: new[] { "SecurityId", "PeriodType", "Period", "MetricType", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMetrics_SourceId",
                table: "FinancialMetrics",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchScores_SecurityId_Dimension",
                table: "ResearchScores",
                columns: new[] { "SecurityId", "Dimension" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchSources_SecurityId",
                table: "ResearchSources",
                column: "SecurityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyResearches");

            migrationBuilder.DropTable(
                name: "FinancialMetrics");

            migrationBuilder.DropTable(
                name: "ResearchScores");

            migrationBuilder.DropTable(
                name: "ResearchSources");
        }
    }
}
