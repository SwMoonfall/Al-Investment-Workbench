using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class Security : Entity
{
    private Security() { }
    public Security(string symbol, string name, string exchange, string currency, SecurityType type)
    {
        Symbol = Guard.Text(symbol, nameof(symbol), 32).ToUpperInvariant();
        Name = Guard.Text(name, nameof(name), 200);
        Exchange = Guard.Text(exchange, nameof(exchange), 32).ToUpperInvariant();
        Currency = Guard.Currency(currency);
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        Type = type;
    }
    public string Symbol { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Exchange { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public SecurityType Type { get; private set; }
    public string Ticker => Symbol;
    public string CompanyName => Name;
    public SecurityType SecurityType => Type;
    public Market Market { get; private set; } = Market.Other;
    public string Sector { get; private set; } = "";
    public string Industry { get; private set; } = "";
    public string Country { get; private set; } = "";
    public string? ISIN { get; private set; }
    public string Notes { get; private set; } = "";
    public decimal? LatestPrice { get; private set; }
    public DateTimeOffset? PriceDate { get; private set; }
    public void Edit(string ticker, string name, string exchange, string currency, SecurityType type,
        Market market, string sector, string industry, string country, string? isin, string notes)
    {
        var valid = new Security(ticker, name, exchange, currency, type);
        if (!Enum.IsDefined(market)) throw new BusinessException("无效市场。");
        var code = Guard.OptionalText(isin, 12).ToUpperInvariant();
        if (code.Length != 0 && (code.Length != 12 || code.Any(c => !char.IsAsciiLetterOrDigit(c))))
            throw new BusinessException("ISIN 应为 12 位字母或数字，或留空。");
        Symbol = valid.Symbol; Name = valid.Name; Exchange = valid.Exchange; Currency = valid.Currency; Type = type;
        Market = market; Sector = Guard.OptionalText(sector, 100); Industry = Guard.OptionalText(industry, 100);
        Country = Guard.OptionalText(country, 100); ISIN = code.Length == 0 ? null : code; Notes = Guard.OptionalText(notes);
        MarkUpdated();
    }
    public void SetPrice(decimal price, DateTimeOffset date)
    {
        if (price < 0) throw new BusinessException("价格不能为负数。");
        if (date == default || date > DateTimeOffset.UtcNow) throw new BusinessException("报价日期不能为空或晚于当前时间。");
        LatestPrice = price; PriceDate = date.ToUniversalTime(); MarkUpdated();
    }
    public ICollection<Position> Positions { get; private set; } = new List<Position>();
    public ICollection<WatchlistItem> WatchlistItems { get; private set; } = new List<WatchlistItem>();
    public ICollection<Transaction> Transactions { get; private set; } = new List<Transaction>();
}
