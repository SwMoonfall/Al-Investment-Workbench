using System.Security.Cryptography;
using System.Text;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Infrastructure.Storage;

public sealed class WindowsSecretStore(AppPaths paths) : ISecretStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string FilePath(string scope) => Path.Combine(paths.Root, "Secrets", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope))) + ".dpapi");
    public async Task<string?> ReadAsync(string scope, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new BusinessException("此密钥存储需要 Windows。");
        await _gate.WaitAsync(ct);
        try
        {
            var path = FilePath(scope); if (!File.Exists(path)) return null;
            var plain = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path, ct), Encoding.UTF8.GetBytes(scope), DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); } finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (CryptographicException) { throw new BusinessException("密钥无法解密，请在当前 Windows 用户下重新保存。"); }
        finally { _gate.Release(); }
    }
    public async Task WriteAsync(string scope, string secret, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new BusinessException("此密钥存储需要 Windows。");
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 4096 || secret.Any(char.IsControl)) throw new BusinessException("API Key 无效。");
        await _gate.WaitAsync(ct); var plain = Encoding.UTF8.GetBytes(secret.Trim()); var temp = FilePath(scope) + ".tmp";
        try { Directory.CreateDirectory(Path.GetDirectoryName(temp)!); var cipher = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes(scope), DataProtectionScope.CurrentUser); await File.WriteAllBytesAsync(temp, cipher, ct); File.Move(temp, FilePath(scope), true); }
        finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(temp)) File.Delete(temp); _gate.Release(); }
    }
    public async Task DeleteAsync(string scope, CancellationToken ct = default)
    { await _gate.WaitAsync(ct); try { File.Delete(FilePath(scope)); } finally { _gate.Release(); } }
}
