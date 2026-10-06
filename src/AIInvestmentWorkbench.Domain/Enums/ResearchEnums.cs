namespace AIInvestmentWorkbench.Domain.Enums;

public enum PeriodType { Annual, Quarterly, TTM }
public enum MetricType
{
    Revenue, RevenueGrowth, GrossProfit, GrossMargin, OperatingIncome, OperatingMargin,
    NetIncome, EPS, OperatingCashFlow, Capex, FreeCashFlow, Cash, Debt, NetDebt, Equity,
    ROE, ROIC, ShareCount, StockBasedCompensation, Inventory, AccountsReceivable
}
public enum SourceType
{
    AnnualReport, QuarterlyReport, ExchangeFiling, InvestorPresentation, EarningsCall,
    CompanyWebsite, FinancialDatabase, News, BrokerResearch, ExpertOpinion, SocialMedia,
    UserNotes, CSV, PDF, Other
}
public enum ScoreDimension { BusinessModel, CompetitiveAdvantage, FinancialQuality, ManagementCapitalAllocation, GrowthPotential, Valuation, RiskUnderstandability }
public enum AttentionLevel { Normal, Warning, HighAttention }
