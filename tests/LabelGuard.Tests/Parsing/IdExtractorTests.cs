using LabelGuard.Core.Parsing;

namespace LabelGuard.Tests.Parsing;

public class IdExtractorTests
{
    [Theory]
    [InlineData("^XA^FO50,50^BCN,100^FD1196924^FS^XZ", "1196924")]
    [InlineData("BARCODE 100,100,\"128\",50,1,0,2,2,\"1196924\"\r\nPRINT 1\r\n", "1196924")]
    [InlineData("LOTE 12112026 PALETE 1196924", "1196924")]
    public void DefaultPattern_FindsSevenDigitId(string text, string expected)
    {
        Assert.Equal(expected, new IdExtractor().Extract(text));
    }

    [Theory]
    [InlineData("^XA^FO50,50^FD123456^FS^XZ")]
    [InlineData("^XA^FD12345678^FS^XZ")]
    [InlineData("")]
    public void DefaultPattern_ReturnsNullWithoutId(string text)
    {
        Assert.Null(new IdExtractor().Extract(text));
    }

    [Fact]
    public void CustomPattern_IsUsed()
    {
        var extractor = new IdExtractor(@"ID:(?<id>\d+)");

        Assert.Equal("42", extractor.Extract("CODIGO 1196924 ID:42"));
    }

    [Fact]
    public void PatternWithoutIdGroup_Throws()
    {
        Assert.Throws<ArgumentException>(() => new IdExtractor(@"\d+"));
    }

    [Fact]
    public void InvalidPattern_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new IdExtractor("(?<id>"));
    }
}
