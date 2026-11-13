using LabelGuard.Core.Parsing;

namespace LabelGuard.Tests.Parsing;

public class IdExtractorTests
{
    private const string PapersystemQr =
        "https://app.papersystem.com.br/d/etiqueta?pallet=3931&fornecedor=CLIENTE%20TESTE&largura=1500&t=1";

    // Etiqueta interna (Produção: ADFLOG): o QR traz JSON em vez do link.
    private const string PapersystemJsonQr =
        "{\"fornecedor\":\"ADF\",\"largura\":\"1830\",\"gramatura\":\"120\",\"pallet\":\"1415\",\"codigocliente\":\"MIOLO\",\"peso\":\"1559.00\",\"quantidadepc\":\"1\",\"t\":1}";

    [Theory]
    [InlineData(PapersystemQr, "3931")]
    [InlineData("https://app.papersystem.com.br/d/etiqueta?t=1&pallet=42", "42")]
    [InlineData(PapersystemJsonQr, "1415")]
    [InlineData("{\"pallet\": 77, \"t\": 1}", "77")]
    public void DefaultPattern_FindsPalletNumber(string text, string expected)
    {
        Assert.Equal(expected, new IdExtractor().Extract(text));
    }

    [Theory]
    [InlineData("https://app.papersystem.com.br/d/etiqueta?largura=1500")]
    [InlineData("PALLET 3931")]
    [InlineData("?codigopallet=3931")]
    [InlineData("{\"codigopallet\":\"3931\"}")]
    [InlineData("")]
    public void DefaultPattern_ReturnsNullWithoutId(string text)
    {
        Assert.Null(new IdExtractor().Extract(text));
    }

    [Fact]
    public void Label_ReadsIdFromQrCodeInBitmap()
    {
        var label = Assert.Single(LabelSplitter.Split(TsplJob.WithQrCode(PapersystemQr)));

        Assert.Equal("3931", new IdExtractor().Extract(label));
    }

    [Fact]
    public void Label_ReadsIdFromJsonQrCodeInBitmap()
    {
        var label = Assert.Single(LabelSplitter.Split(TsplJob.WithQrCode(PapersystemJsonQr)));

        Assert.Equal("1415", new IdExtractor().Extract(label));
    }

    [Fact]
    public void Label_WithoutQrCode_FallsBackToCommandText()
    {
        var label = Assert.Single(LabelSplitter.Split(System.Text.Encoding.Latin1.GetBytes(
            "^XA^FDhttps://x/etiqueta?pallet=77^FS^XZ")));

        Assert.Equal("77", new IdExtractor().Extract(label));
    }

    [Fact]
    public void Label_BlankBitmap_ReturnsNull()
    {
        var job = TsplJob.WithBitmap(10, 10, Enumerable.Repeat((byte)0xFF, 100).ToArray());

        Assert.Null(new IdExtractor().Extract(Assert.Single(LabelSplitter.Split(job))));
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
