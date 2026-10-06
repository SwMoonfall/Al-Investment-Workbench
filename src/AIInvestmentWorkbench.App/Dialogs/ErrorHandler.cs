using System.Windows;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AIInvestmentWorkbench.App.Dialogs;

public interface IErrorHandler { void Report(Exception exception, string userMessage); }

public sealed class ErrorHandler(ILogger<ErrorHandler> logger) : IErrorHandler
{
    public void Report(Exception exception, string userMessage)
    {
        logger.LogError(exception, "Operation failed. See exception type and stack below.");
        userMessage = exception switch { FileNotFoundException => "找不到所选文件。文件可能已移动或删除，请重新选择。", DirectoryNotFoundException => "目录不存在，请选择可用的保存目录。", UnauthorizedAccessException => "没有此目录的读写权限。请选择当前用户可访问的目录。", SqliteException => "数据库操作失败。请检查磁盘空间和文件权限；不要删除数据库，可从设置中的备份恢复。", IOException => "文件操作失败，可能被占用或磁盘空间不足。请关闭占用程序并检查保存位置。", OperationCanceledException => "操作已取消。", _ => userMessage };
        System.Windows.Application.Current.Dispatcher.Invoke(() => MessageBox.Show(userMessage, "AI 投资工作台", MessageBoxButton.OK, MessageBoxImage.Warning));
    }
}

