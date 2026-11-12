using System.Text;
using LabelGuard.Core.Parsing;

namespace LabelGuard.Tests.Parsing;

public class LabelSplitterTests
{
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    private static byte[] Concat(IEnumerable<Label> labels) => labels.SelectMany(l => l.RawData).ToArray();

    [Fact]
    public void EmptyJob_ReturnsNoLabels()
    {
        Assert.Empty(LabelSplitter.Split(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData("^XA^FO50,50^FD1196924^FS^XZ", PrinterLanguage.Zpl)]
    [InlineData("SIZE 100 mm,50 mm\r\nCLS\r\nPRINT 1\r\n", PrinterLanguage.Tspl)]
    [InlineData("texto qualquer 1196924", PrinterLanguage.Unknown)]
    public void Detect_IdentifiesLanguage(string job, PrinterLanguage expected)
    {
        Assert.Equal(expected, LabelSplitter.Detect(job));
    }

    [Fact]
    public void Zpl_SplitsEachLabel()
    {
        var job = "^XA^FD1196924^FS^XZ^XA^FD1196925^FS^XZ";

        var labels = LabelSplitter.Split(Bytes(job));

        Assert.Equal(2, labels.Count);
        Assert.Equal("^XA^FD1196924^FS^XZ", labels[0].Text);
        Assert.Equal("^XA^FD1196925^FS^XZ", labels[1].Text);
        Assert.All(labels, l => Assert.Equal(1, l.Copies));
    }

    [Fact]
    public void Zpl_KeepsLeadingAndTrailingContent()
    {
        var job = "~JA\r\n^XA^FD1196924^FS^XZ\r\n^XA^FD1196925^FS^XZ\r\n";

        var labels = LabelSplitter.Split(Bytes(job));

        Assert.Equal(2, labels.Count);
        Assert.StartsWith("~JA", labels[0].Text);
        Assert.EndsWith("^XZ\r\n", labels[1].Text);
        Assert.Equal(Bytes(job), Concat(labels));
    }

    [Fact]
    public void Zpl_ReadsQuantity()
    {
        var labels = LabelSplitter.Split(Bytes("^XA^FD1196924^FS^PQ3^XZ"));

        Assert.Equal(3, Assert.Single(labels).Copies);
    }

    [Fact]
    public void Tspl_SplitsAtPrintCommand()
    {
        var job =
            "SIZE 100 mm,50 mm\r\nGAP 2 mm,0\r\nCLS\r\nBARCODE 100,100,\"128\",50,1,0,2,2,\"1196924\"\r\nPRINT 1\r\n" +
            "CLS\r\nBARCODE 100,100,\"128\",50,1,0,2,2,\"1196925\"\r\nPRINT 1,1\r\n";

        var labels = LabelSplitter.Split(Bytes(job));

        Assert.Equal(2, labels.Count);
        Assert.StartsWith("SIZE", labels[0].Text);
        Assert.EndsWith("PRINT 1\r\n", labels[0].Text);
        Assert.StartsWith("CLS", labels[1].Text);
        Assert.Equal(Bytes(job), Concat(labels));
    }

    [Theory]
    [InlineData("CLS\nprint 2\n", 2)]
    [InlineData("CLS\r\nPRINT 2,3\r\n", 6)]
    [InlineData("CLS\r\nPRINT 0", 1)]
    public void Tspl_ReadsCopies(string job, int expected)
    {
        Assert.Equal(expected, Assert.Single(LabelSplitter.Split(Bytes(job))).Copies);
    }

    [Fact]
    public void Tspl_IgnoresPrintInsideOtherCommands()
    {
        var job = "CLS\r\nTEXT 10,10,\"3\",0,1,1,\"PRINT 5\"\r\nPRINT 1\r\n";

        var label = Assert.Single(LabelSplitter.Split(Bytes(job)));

        Assert.Equal(1, label.Copies);
    }

    [Fact]
    public void Unknown_ReturnsWholeJobAsOneLabel()
    {
        var job = Bytes("dados sem comandos conhecidos");

        var label = Assert.Single(LabelSplitter.Split(job));

        Assert.Equal(job, label.RawData);
        Assert.Equal(1, label.Copies);
    }

    [Fact]
    public void BinaryBytes_ArePreserved()
    {
        var job = new byte[] { 0x5E, 0x58, 0x41, 0x00, 0xFF, 0x80, 0x5E, 0x58, 0x5A, 0x0D, 0x0A }; // ^XA .. ^XZ\r\n

        var label = Assert.Single(LabelSplitter.Split(job));

        Assert.Equal(job, label.RawData);
    }
}
