using System.Text;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Rules;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
namespace AIInvestmentWorkbench.Infrastructure.Storage;

public sealed class PdfTextExtractor : IPdfTextExtractor
{
    public string Extract(string path)
    {
        try
        {
            using var document = PdfDocument.Open(path);
            if (document.NumberOfPages > 1000) throw new BusinessException("PDF 超过 1000 页，请拆分后导入。");
            var result = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                result.AppendLine(ContentOrderTextExtractor.GetText(page));
                if (result.Length > 2000000) throw new BusinessException("提取文本超过 200 万字符，请拆分文件。");
            }
            return result.ToString();
        }
        catch (BusinessException) { throw; }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        { throw new BusinessException("无法读取此 PDF，文件可能损坏或受密码保护。请提供可正常打开、未加密的 PDF。"); }
    }
}
public sealed class ResearchFileReader(IPdfTextExtractor pdf) : IResearchFileReader
{
    public const string NoPdfTextMessage = "该PDF可能是扫描件，当前版本不支持OCR。";
    public async Task<string> ReadAsync(string path, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".txt" or ".md" or ".markdown" or ".csv" or ".pdf")) throw new BusinessException("仅支持 TXT、Markdown、CSV 和 PDF 文件。");
        var info = new FileInfo(path);
        if (!info.Exists) throw new BusinessException("文件不存在，请重新选择。");
        if (info.Length > 20 * 1024 * 1024) throw new BusinessException("文件超过 20 MB，请拆分后导入。");
        try
        {
            var text = extension == ".pdf" ? await Task.Run(() => pdf.Extract(path), ct) : await File.ReadAllTextAsync(path, new UTF8Encoding(false, true), ct);
            ct.ThrowIfCancellationRequested();
            return ValidateText(text, extension == ".pdf");
        }
        catch (DecoderFallbackException) { throw new BusinessException("文件不是有效的 UTF-8 文本，请转换编码后重试。"); }
        catch (IOException) { throw new BusinessException("无法读取文件，请检查文件是否可访问或被其他程序占用。"); }
        catch (UnauthorizedAccessException) { throw new BusinessException("没有权限读取此文件，请选择可访问的文件。"); }
    }
    public static string ValidateText(string text, bool isPdf)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new BusinessException(isPdf ? NoPdfTextMessage : "文件没有可导入的文本。");
        if (text.Length > 2000000) throw new BusinessException("文本超过 200 万字符，请拆分文件。");
        return text.Trim();
    }
}
