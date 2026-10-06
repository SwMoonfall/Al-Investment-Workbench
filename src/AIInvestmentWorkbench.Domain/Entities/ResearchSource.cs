using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class ResearchSource : Entity
{
    private ResearchSource() { }
    public ResearchSource(Guid securityId, string title, string publisher, DateTimeOffset? publishedDate, string url,
        string localFilePath, SourceType sourceType, int reliabilityLevel, string notes = "", string extractedText = "")
    {
        SecurityId = Guard.Id(securityId, nameof(securityId));
        Edit(title, publisher, publishedDate, url, localFilePath, sourceType, reliabilityLevel, notes, extractedText);
    }
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public string Title { get; private set; } = "";
    public string Publisher { get; private set; } = "";
    public DateTimeOffset? PublishedDate { get; private set; }
    public string Url { get; private set; } = "";
    public string LocalFilePath { get; private set; } = "";
    public SourceType SourceType { get; private set; }
    public int ReliabilityLevel { get; private set; }
    public string Notes { get; private set; } = "";
    public string ExtractedText { get; private set; } = "";
    public void Edit(string title, string publisher, DateTimeOffset? publishedDate, string url, string path,
        SourceType type, int reliability, string notes, string text)
    {
        if (reliability is < 1 or > 6) throw new BusinessException("来源等级必须是 1 至 6，1 为最高可信度；等级由用户核实。");
        if (!Enum.IsDefined(type)) throw new BusinessException("未知来源类型。");
        if (publishedDate > DateTimeOffset.Now) throw new BusinessException("发布日期不能晚于今天。");
        url = Guard.OptionalText(url, 2000);
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) throw new BusinessException("来源链接应为 HTTP 或 HTTPS 地址。");
        Title = Guard.Text(title, nameof(title), 300); Publisher = Guard.OptionalText(publisher, 300); PublishedDate = publishedDate;
        Url = url; LocalFilePath = Guard.OptionalText(path, 2000); SourceType = type; ReliabilityLevel = reliability;
        Notes = Guard.OptionalText(notes, 10000); ExtractedText = Guard.OptionalText(text, 2000000); MarkUpdated();
    }
}
