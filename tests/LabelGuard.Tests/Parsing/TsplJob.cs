using System.Text;
using ZXing;
using ZXing.QrCode;

namespace LabelGuard.Tests.Parsing;

/// <summary>Monta trabalhos TSPL como os do driver Windows: a etiqueta inteira em um BITMAP.</summary>
internal static class TsplJob
{
    public static byte[] WithBitmap(int widthBytes, int height, byte[] data, string print = "PRINT 1,1")
    {
        var header = Encoding.Latin1.GetBytes(
            "SIZE 105 mm,210 mm\r\nGAP 3 mm,0 mm\r\nCLS\r\n" + $"BITMAP 0,0,{widthBytes},{height},1,");
        var footer = Encoding.Latin1.GetBytes($"\r\n{print}\r\n");
        return header.Concat(data).Concat(footer).ToArray();
    }

    /// <summary>Etiqueta branca com um QR code contendo <paramref name="content"/>.</summary>
    public static byte[] WithQrCode(string content, string print = "PRINT 1,1")
    {
        const int widthBytes = 60, height = 480, offset = 40, size = 400;
        var data = Enumerable.Repeat((byte)0xFF, widthBytes * height).ToArray();
        var qr = new QRCodeWriter().encode(content, BarcodeFormat.QR_CODE, size, size);
        for (var y = 0; y < qr.Height; y++)
            for (var x = 0; x < qr.Width; x++)
                if (qr[x, y])
                {
                    int px = x + offset, py = y + offset;
                    data[py * widthBytes + px / 8] &= (byte)~(1 << (7 - px % 8));
                }
        return WithBitmap(widthBytes, height, data, print);
    }
}
