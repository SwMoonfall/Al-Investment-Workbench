using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInvestmentWorkbench.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortfolioRiskDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortfolioAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Choice = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    AnswersJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContextJson = table.Column<string>(type: "TEXT", nullable: false),
                    SizingJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionRecords", x => x.Id);
                    table.CheckConstraint("CK_Decision_Choice", "(Kind = 0 AND Choice IN (0,4)) OR (Kind = 1 AND Choice IN (1,2,3,4))");
                    table.ForeignKey(
                        name: "FK_DecisionRecords_PortfolioAccounts_PortfolioAccountId",
                        column: x => x.PortfolioAccountId,
                        principalTable: "PortfolioAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DecisionRecords_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RiskTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskTags", x => x.Id);
                    table.CheckConstraint("CK_RiskTag_Category", "Category BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "SecurityRiskTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RiskTagId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityRiskTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityRiskTags_RiskTags_RiskTagId",
                        column: x => x.RiskTagId,
                        principalTable: "RiskTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SecurityRiskTags_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionRecords_PortfolioAccountId_SecurityId",
                table: "DecisionRecords",
                columns: new[] { "PortfolioAccountId", "SecurityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionRecords_SecurityId",
                table: "DecisionRecords",
                column: "SecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskTags_Category_NormalizedName",
                table: "RiskTags",
                columns: new[] { "Category", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRiskTags_RiskTagId",
                table: "SecurityRiskTags",
                column: "RiskTagId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRiskTags_SecurityId_RiskTagId",
                table: "SecurityRiskTags",
                columns: new[] { "SecurityId", "RiskTagId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionRecords");

            migrationBuilder.DropTable(
                name: "SecurityRiskTags");

            migrationBuilder.DropTable(
                name: "RiskTags");
        }
    }
}
