using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInvestmentWorkbench.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JournalAndReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PortfolioAccountId",
                table: "AIAnalyses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvestmentJournals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortfolioAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecisionRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Action = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: true),
                    PortfolioWeight = table.Column<decimal>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    MarketConcern = table.Column<string>(type: "TEXT", nullable: false),
                    WhyMarketMayBeWrong = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedDevelopment = table.Column<string>(type: "TEXT", nullable: false),
                    KillConditionSummary = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedHoldingPeriod = table.Column<string>(type: "TEXT", nullable: false),
                    Emotion = table.Column<string>(type: "TEXT", nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedHoldingDays = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentJournals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentJournals_DecisionRecords_DecisionRecordId",
                        column: x => x.DecisionRecordId,
                        principalTable: "DecisionRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentJournals_PortfolioAccounts_PortfolioAccountId",
                        column: x => x.PortfolioAccountId,
                        principalTable: "PortfolioAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentJournals_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortfolioAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RootId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ActualResults = table.Column<string>(type: "TEXT", nullable: false),
                    AssumptionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: true),
                    Conclusion = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentReviews_PortfolioAccounts_PortfolioAccountId",
                        column: x => x.PortfolioAccountId,
                        principalTable: "PortfolioAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentReviews_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIAnalyses_PortfolioAccountId",
                table: "AIAnalyses",
                column: "PortfolioAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentJournals_DecisionRecordId",
                table: "InvestmentJournals",
                column: "DecisionRecordId",
                unique: true,
                filter: "DecisionRecordId IS NOT NULL AND Version = 1");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentJournals_PortfolioAccountId",
                table: "InvestmentJournals",
                column: "PortfolioAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentJournals_RootId_Version",
                table: "InvestmentJournals",
                columns: new[] { "RootId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentJournals_SecurityId",
                table: "InvestmentJournals",
                column: "SecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentReviews_PortfolioAccountId",
                table: "InvestmentReviews",
                column: "PortfolioAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentReviews_RootId_Version",
                table: "InvestmentReviews",
                columns: new[] { "RootId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentReviews_SecurityId",
                table: "InvestmentReviews",
                column: "SecurityId");

            migrationBuilder.AddForeignKey(
                name: "FK_AIAnalyses_PortfolioAccounts_PortfolioAccountId",
                table: "AIAnalyses",
                column: "PortfolioAccountId",
                principalTable: "PortfolioAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AIAnalyses_PortfolioAccounts_PortfolioAccountId",
                table: "AIAnalyses");

            migrationBuilder.DropTable(
                name: "InvestmentJournals");

            migrationBuilder.DropTable(
                name: "InvestmentReviews");

            migrationBuilder.DropIndex(
                name: "IX_AIAnalyses_PortfolioAccountId",
                table: "AIAnalyses");

            migrationBuilder.DropColumn(
                name: "PortfolioAccountId",
                table: "AIAnalyses");
        }
    }
}
