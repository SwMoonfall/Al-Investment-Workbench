using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInvestmentWorkbench.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ValuationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ValuationModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ModelType = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Revenue = table.Column<decimal>(type: "TEXT", nullable: false),
                    NetDebt = table.Column<decimal>(type: "TEXT", nullable: false),
                    ShareCount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    DataSourceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PriceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValuationModels", x => x.Id);
                    table.CheckConstraint("CK_ValuationModel", "Revision >= 1 AND ModelType BETWEEN 0 AND 4 AND CAST(ShareCount AS REAL) > 0 AND CAST(CurrentPrice AS REAL) > 0");
                    table.ForeignKey(
                        name: "FK_ValuationModels_Securities_SecurityId",
                        column: x => x.SecurityId,
                        principalTable: "Securities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValuationScenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ValuationModelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    InputsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AssumptionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValuationScenarios", x => x.Id);
                    table.CheckConstraint("CK_ValuationScenario", "Kind BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_ValuationScenarios_ValuationModels_ValuationModelId",
                        column: x => x.ValuationModelId,
                        principalTable: "ValuationModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ValuationModels_SecurityId",
                table: "ValuationModels",
                column: "SecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_ValuationScenarios_ValuationModelId_Kind",
                table: "ValuationScenarios",
                columns: new[] { "ValuationModelId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ValuationScenarios");

            migrationBuilder.DropTable(
                name: "ValuationModels");
        }
    }
}
