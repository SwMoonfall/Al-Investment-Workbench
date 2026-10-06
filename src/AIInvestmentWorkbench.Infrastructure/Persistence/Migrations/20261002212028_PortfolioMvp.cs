using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInvestmentWorkbench.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortfolioMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transaction_Amounts",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transaction_Type",
                table: "Transactions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastResearchDate",
                table: "WatchlistItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextAction",
                table: "WatchlistItems",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "WatchlistItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "WatchlistItems",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RiskStatus",
                table: "WatchlistItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "WatchlistItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Transactions",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Transactions",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Securities",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ISIN",
                table: "Securities",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "Securities",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "LatestPrice",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Market",
                table: "Securities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Securities",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PriceDate",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sector",
                table: "Securities",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Bucket",
                table: "Positions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxWeight",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetWeight",
                table: "Positions",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentCash",
                table: "PortfolioAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultMaxPositionWeight",
                table: "PortfolioAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InitialCapital",
                table: "PortfolioAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetActiveWeight",
                table: "PortfolioAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetCashWeight",
                table: "PortfolioAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetEtfWeight",
                table: "PortfolioAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetExperimentalWeight",
                table: "PortfolioAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transaction_Amounts",
                table: "Transactions",
                sql: "CAST(Fees AS REAL) >= 0 AND ((Type IN (1,2) AND SecurityId IS NOT NULL AND CAST(Quantity AS REAL) > 0 AND CAST(UnitPrice AS REAL) > 0 AND CAST(CashAmount AS REAL) = 0) OR (Type IN (3,4,5,6) AND (SecurityId IS NULL OR Type = 5) AND CAST(Quantity AS REAL) = 0 AND CAST(UnitPrice AS REAL) = 0 AND CAST(CashAmount AS REAL) > 0 AND (Type <> 6 OR CAST(Fees AS REAL) = 0)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transaction_Type",
                table: "Transactions",
                sql: "Type IN (1,2,3,4,5,6)");
            migrationBuilder.Sql("UPDATE Transactions SET Currency = (SELECT Currency FROM PortfolioAccounts WHERE Id = Transactions.PortfolioAccountId)");
            migrationBuilder.Sql("UPDATE Securities SET Type = 3 WHERE Type = 4");
            migrationBuilder.Sql("UPDATE Securities SET Market = 3");
            migrationBuilder.Sql("UPDATE Positions SET Bucket = CASE WHEN (SELECT Type FROM Securities WHERE Id = Positions.SecurityId) = 2 THEN 0 ELSE 1 END, MaxWeight = '0.1'");
            migrationBuilder.Sql("UPDATE PortfolioAccounts SET TargetCashWeight = '0.1', TargetEtfWeight = '0.4', TargetActiveWeight = '0.4', TargetExperimentalWeight = '0.1', DefaultMaxPositionWeight = '0.1'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transaction_Amounts",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transaction_Type",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LastResearchDate",
                table: "WatchlistItems");

            migrationBuilder.DropColumn(
                name: "NextAction",
                table: "WatchlistItems");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "WatchlistItems");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "WatchlistItems");

            migrationBuilder.DropColumn(
                name: "RiskStatus",
                table: "WatchlistItems");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "WatchlistItems");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "ISIN",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "LatestPrice",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "Market",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "PriceDate",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "Sector",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "Bucket",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "MaxWeight",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "TargetWeight",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "CurrentCash",
                table: "PortfolioAccounts");

            migrationBuilder.DropColumn(
                name: "DefaultMaxPositionWeight",
                table: "PortfolioAccounts");

            migrationBuilder.DropColumn(
                name: "InitialCapital",
                table: "PortfolioAccounts");

            migrationBuilder.DropColumn(
                name: "TargetActiveWeight",
                table: "PortfolioAccounts");

            migrationBuilder.DropColumn(
                name: "TargetCashWeight",
                table: "PortfolioAccounts");

            migrationBuilder.DropColumn(
                name: "TargetEtfWeight",
                table: "PortfolioAccounts");

            migrationBuilder.DropColumn(
                name: "TargetExperimentalWeight",
                table: "PortfolioAccounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transaction_Amounts",
                table: "Transactions",
                sql: "CAST(Fees AS REAL) >= 0 AND ((Type IN (1,2) AND SecurityId IS NOT NULL AND CAST(Quantity AS REAL) > 0 AND CAST(UnitPrice AS REAL) > 0 AND CAST(CashAmount AS REAL) = 0) OR (Type IN (3,4) AND SecurityId IS NULL AND CAST(Quantity AS REAL) = 0 AND CAST(UnitPrice AS REAL) = 0 AND CAST(CashAmount AS REAL) > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transaction_Type",
                table: "Transactions",
                sql: "Type IN (1,2,3,4)");
        }
    }
}

