using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Rules;
using Xunit;
namespace AIInvestmentWorkbench.Application.Tests;
public class CsvParserTests
{
    [Fact]
    public void QuotedCommaEscapedQuotesAndMultiline_ArePreserved()
    {
        var doc = CsvImportService.Parse("\uFEFFTicker,Notes\r\nABC,\"one,two\n\"\"quoted\"\"\"\r\n");
        Assert.Equal("Ticker", doc.Headers[0]); Assert.Equal("one,two\n\"quoted\"", Assert.Single(doc.Rows)[1]);
    }
    [Theory]
    [InlineData("A,A\n1,2")]
    [InlineData("A,B\n1")]
    [InlineData("A,B\n\"unfinished,2")]
    [InlineData("A,B\n\"closed\"bad,2")]
    [InlineData("A,B")]
    public void MalformedFiles_AreRejected(string csv) => Assert.Throws<BusinessException>(() => CsvImportService.Parse(csv));
}
