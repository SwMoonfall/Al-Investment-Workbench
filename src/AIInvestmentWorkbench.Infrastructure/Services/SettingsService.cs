using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class SettingsService(IDbContextFactory<InvestmentDbContext> factory) : ISettingsService
{
    private const string ThemeKey = "Appearance.Theme";
    public async Task<AppTheme> GetThemeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var value = await context.AppSettings.AsNoTracking().Where(x => x.Key == ThemeKey).Select(x => x.Value).SingleOrDefaultAsync(cancellationToken);
        return Enum.TryParse<AppTheme>(value, out var theme) && Enum.IsDefined(theme) ? theme : AppTheme.Light;
    }
    public async Task SaveThemeAsync(AppTheme theme, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var setting = await context.AppSettings.SingleOrDefaultAsync(x => x.Key == ThemeKey, cancellationToken);
        if (setting is null) context.AppSettings.Add(new AppSetting(ThemeKey, theme.ToString()));
        else setting.SetValue(theme.ToString());
        await context.SaveChangesAsync(cancellationToken);
    }
}
