namespace AIInvestmentWorkbench.Infrastructure.Storage;

public sealed class AppPaths
{
    public AppPaths(string? root = null, bool isDemo = false)
    {
        IsDemo = isDemo; Root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIInvestmentWorkbench", "Workspaces", "Default"));
    }
    public string Root { get; }
    public bool IsDemo { get; }
    public string DatabaseDirectory => Path.Combine(Root, "Data");
    public string DatabasePath => Path.Combine(DatabaseDirectory, "investment.db");
    public string LogsDirectory => Path.Combine(Root, "Logs");
    public string BackupsDirectory => Path.Combine(Root, "Backups");
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}

