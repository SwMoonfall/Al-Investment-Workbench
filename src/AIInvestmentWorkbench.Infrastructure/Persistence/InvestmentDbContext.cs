using AIInvestmentWorkbench.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIInvestmentWorkbench.Infrastructure.Persistence;

public sealed class InvestmentDbContext(DbContextOptions<InvestmentDbContext> options) : DbContext(options)
{
    public DbSet<PortfolioAccount> PortfolioAccounts => Set<PortfolioAccount>();
    public DbSet<Security> Securities => Set<Security>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<CompanyResearch> CompanyResearches => Set<CompanyResearch>();
    public DbSet<FinancialMetric> FinancialMetrics => Set<FinancialMetric>();
    public DbSet<ResearchSource> ResearchSources => Set<ResearchSource>();
    public DbSet<ResearchScore> ResearchScores => Set<ResearchScore>();
    public DbSet<Thesis> Theses => Set<Thesis>();
    public DbSet<ThesisAssumption> ThesisAssumptions => Set<ThesisAssumption>();
    public DbSet<KillCondition> KillConditions => Set<KillCondition>();
    public DbSet<ThesisVersion> ThesisVersions => Set<ThesisVersion>();
    public DbSet<ValuationModel> ValuationModels => Set<ValuationModel>();
    public DbSet<ValuationScenario> ValuationScenarios => Set<ValuationScenario>();
    public DbSet<RiskTag> RiskTags => Set<RiskTag>();
    public DbSet<SecurityRiskTag> SecurityRiskTags => Set<SecurityRiskTag>();
    public DbSet<DecisionRecord> DecisionRecords => Set<DecisionRecord>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<AIAnalysis> AIAnalyses => Set<AIAnalysis>();
    public DbSet<AIResearchReport> AIResearchReports => Set<AIResearchReport>();
    public DbSet<InvestmentJournal> InvestmentJournals => Set<InvestmentJournal>();
    public DbSet<InvestmentReview> InvestmentReviews => Set<InvestmentReview>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<InvestmentJournal>(b =>
        {
            b.Ignore(x => x.Content); b.HasIndex(x => new { x.RootId, x.Version }).IsUnique();
            b.HasIndex(x => x.DecisionRecordId).IsUnique().HasFilter("DecisionRecordId IS NOT NULL AND Version = 1");
            b.HasOne<PortfolioAccount>().WithMany().HasForeignKey(x => x.PortfolioAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DecisionRecord>().WithMany().HasForeignKey(x => x.DecisionRecordId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<InvestmentReview>(b =>
        {
            b.HasIndex(x => new { x.RootId, x.Version }).IsUnique();
            b.HasOne<PortfolioAccount>().WithMany().HasForeignKey(x => x.PortfolioAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PromptTemplate>(b => { b.HasIndex(x => x.AnalysisType).IsUnique().HasFilter("IsBuiltIn = 1"); b.Property(x => x.Revision).IsConcurrencyToken(); });
        model.Entity<AIAnalysis>(b =>
        {
            b.HasOne<PortfolioAccount>().WithMany().HasForeignKey(x => x.PortfolioAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.PromptTemplate).WithMany().HasForeignKey(x => x.PromptTemplateId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AIResearchReport>(b => b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict));
        model.Entity<RiskTag>(b =>
        {
            b.HasIndex(x => new { x.Category, x.NormalizedName }).IsUnique();
            b.ToTable(t => t.HasCheckConstraint("CK_RiskTag_Category", "Category BETWEEN 0 AND 4"));
        });
        model.Entity<SecurityRiskTag>(b =>
        {
            b.HasIndex(x => new { x.SecurityId, x.RiskTagId }).IsUnique();
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.RiskTag).WithMany().HasForeignKey(x => x.RiskTagId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DecisionRecord>(b =>
        {
            b.HasIndex(x => new { x.PortfolioAccountId, x.SecurityId });
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.PortfolioAccount).WithMany().HasForeignKey(x => x.PortfolioAccountId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_Decision_Choice", "(Kind = 0 AND Choice IN (0,4)) OR (Kind = 1 AND Choice IN (1,2,3,4))"));
        });
        model.Entity<ValuationModel>(b =>
        {
            b.Ignore(x => x.Actuals); b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_ValuationModel", "Revision >= 1 AND ModelType BETWEEN 0 AND 4 AND CAST(ShareCount AS REAL) > 0 AND CAST(CurrentPrice AS REAL) > 0"));
        });
        model.Entity<ValuationScenario>(b =>
        {
            b.Ignore(x => x.Inputs); b.HasIndex(x => new { x.ValuationModelId, x.Kind }).IsUnique();
            b.HasOne(x => x.ValuationModel).WithMany(x => x.Scenarios).HasForeignKey(x => x.ValuationModelId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_ValuationScenario", "Kind BETWEEN 0 AND 2"));
        });
        model.Entity<Thesis>(b =>
        {
            b.Ignore(x => x.Content); b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => x.SecurityId).IsUnique().HasFilter("IsCurrent = 1");
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_Thesis_Lifecycle", "Status BETWEEN 0 AND 4 AND Version >= 1 AND (IsCurrent = 0 OR (Archived = 0 AND Status IN (1,2))) AND (Status <> 1 OR IsCurrent = 1) AND (Archived = 0 OR Status IN (3,4))"));
        });
        model.Entity<ThesisAssumption>(b =>
        {
            b.HasOne(x => x.Thesis).WithMany(x => x.Assumptions).HasForeignKey(x => x.ThesisId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<KillCondition>(b =>
        {
            b.Ignore(x => x.IsAssessed);
            b.HasOne(x => x.Thesis).WithMany(x => x.KillConditions).HasForeignKey(x => x.ThesisId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ThesisVersion>(b =>
        {
            b.HasIndex(x => new { x.ThesisId, x.Version }).IsUnique();
            b.HasOne(x => x.Thesis).WithMany().HasForeignKey(x => x.ThesisId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CompanyResearch>(b =>
        {
            b.Ignore(x => x.Content);
            b.HasIndex(x => x.SecurityId).IsUnique();
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<FinancialMetric>(b =>
        {
            b.HasIndex(x => new { x.SecurityId, x.PeriodType, x.Period, x.MetricType, x.Currency }).IsUnique();
            b.Property(x => x.Period).HasMaxLength(7); b.Property(x => x.Currency).HasMaxLength(3);
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ResearchSource>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(300);
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_Source_Reliability", "ReliabilityLevel BETWEEN 1 AND 6"));
        });
        model.Entity<ResearchScore>(b =>
        {
            b.Ignore(x => x.EffectiveScore); b.Ignore(x => x.ScoreOrigin);
            b.HasIndex(x => new { x.SecurityId, x.Dimension }).IsUnique();
            b.HasOne(x => x.Security).WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_Score_Ranges", "CAST(Weight AS REAL) BETWEEN 0 AND 100 AND (UserScore IS NULL OR CAST(UserScore AS REAL) BETWEEN 0 AND 100) AND (AIScore IS NULL OR CAST(AIScore AS REAL) BETWEEN 0 AND 100)"));
        });
        model.Entity<PortfolioAccount>(b =>
        {
            b.Ignore(x => x.BaseCurrency);
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        });
        model.Entity<Security>(b =>
        {
            b.Ignore(x => x.Ticker); b.Ignore(x => x.CompanyName); b.Ignore(x => x.SecurityType);
            b.Property(x => x.Sector).HasMaxLength(100); b.Property(x => x.Industry).HasMaxLength(100);
            b.Property(x => x.Country).HasMaxLength(100); b.Property(x => x.ISIN).HasMaxLength(12); b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Exchange).HasMaxLength(32).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.HasIndex(x => new { x.Exchange, x.Symbol }).IsUnique();
            b.ToTable(t => t.HasCheckConstraint("CK_Security_Type", "Type IN (1,2,3,4)"));
        });
        model.Entity<Position>(b =>
        {
            b.Ignore(x => x.AverageCost);
            b.HasIndex(x => new { x.PortfolioAccountId, x.SecurityId }).IsUnique();
            b.HasOne(x => x.PortfolioAccount).WithMany(x => x.Positions).HasForeignKey(x => x.PortfolioAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Security).WithMany(x => x.Positions).HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_Position_Balance", "CAST(Quantity AS REAL) >= 0 AND CAST(TotalCost AS REAL) >= 0 AND (CAST(Quantity AS REAL) > 0 OR CAST(TotalCost AS REAL) = 0)"));
        });
        model.Entity<Transaction>(b =>
        {
            b.Ignore(x => x.Date); b.Ignore(x => x.Price);
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired(); b.Property(x => x.Notes).HasMaxLength(2000);
            b.HasOne(x => x.PortfolioAccount).WithMany(x => x.Transactions).HasForeignKey(x => x.PortfolioAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Security).WithMany(x => x.Transactions).HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.PortfolioAccountId, x.OccurredAt });
            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Transaction_Type", "Type IN (1,2,3,4,5,6)");
                t.HasCheckConstraint("CK_Transaction_Amounts", "CAST(Fees AS REAL) >= 0 AND ((Type IN (1,2) AND SecurityId IS NOT NULL AND CAST(Quantity AS REAL) > 0 AND CAST(UnitPrice AS REAL) > 0 AND CAST(CashAmount AS REAL) = 0) OR (Type IN (3,4,5,6) AND (SecurityId IS NULL OR Type = 5) AND CAST(Quantity AS REAL) = 0 AND CAST(UnitPrice AS REAL) = 0 AND CAST(CashAmount AS REAL) > 0 AND (Type <> 6 OR CAST(Fees AS REAL) = 0)))");
            });
        });
        model.Entity<WatchlistItem>(b =>
        {
            b.Ignore(x => x.Notes);
            b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.NextAction).HasMaxLength(2000);
            b.Property(x => x.Note).HasMaxLength(2000).IsRequired();
            b.HasIndex(x => x.SecurityId).IsUnique();
            b.HasOne(x => x.Security).WithMany(x => x.WatchlistItems).HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AppSetting>(b =>
        {
            b.Property(x => x.Key).HasMaxLength(100).IsRequired();
            b.Property(x => x.Value).HasMaxLength(4000).IsRequired();
            b.HasIndex(x => x.Key).IsUnique();
        });
        foreach (var entity in model.Model.GetEntityTypes())
        {
            model.Entity(entity.ClrType).HasKey(nameof(Entity.Id));
            model.Entity(entity.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }

    private void StampChanges()
    {
        if (ChangeTracker.Entries<InvestmentJournal>().Any(x => x.State is EntityState.Modified or EntityState.Deleted) || ChangeTracker.Entries<InvestmentReview>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Journal and review versions are append-only.");
        foreach (var entry in ChangeTracker.Entries<AIResearchReport>())
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified && entry.Property(x => x.Status).OriginalValue != AIAnalysisStatus.Running)
                throw new InvalidOperationException("Completed AI reports are immutable.");
        foreach (var entry in ChangeTracker.Entries<AIAnalysis>())
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified && entry.Property(x => x.Status).OriginalValue != AIAnalysisStatus.Running)
                throw new InvalidOperationException("Completed AI audit records are immutable.");
        if (ChangeTracker.Entries<DecisionRecord>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Decision history is append-only.");
        if (ChangeTracker.Entries<ThesisVersion>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Thesis history is append-only.");
        foreach (var entry in ChangeTracker.Entries<Entity>().Where(x => x.State == EntityState.Modified))
            entry.Entity.MarkUpdated();
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges(); return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampChanges(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
