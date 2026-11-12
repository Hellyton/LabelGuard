using ZXing;

namespace LabelGuard.Core.Parsing;

/// <summary>Lê QR codes e códigos de barras desenhados nos bitmaps TSPL de uma etiqueta.</summary>
public static class BarcodeScanner
{
    public static IReadOnlyList<string> Read(Label label)
    {
        var texts = new List<string>();
        foreach (var bitmap in TsplBitmap.FindAll(label.Text))
        {
            var source = new RGBLuminanceSource(
                bitmap.ToGray8(label.RawData), bitmap.WidthDots, bitmap.Height, RGBLuminanceSource.BitmapFormat.Gray8);

            // Caminho rápido (~6 ms): só QR, como o do Papersystem. Se falhar, tenta todos os formatos com mais esforço (~40 ms).
            var fast = new BarcodeReaderGeneric { Options = { PossibleFormats = new[] { BarcodeFormat.QR_CODE } } };
            var results = fast.DecodeMultiple(source)
                ?? new BarcodeReaderGeneric { AutoRotate = true, Options = { TryHarder = true } }.DecodeMultiple(source);
            if (results is not null)
                texts.AddRange(results.Select(r => r.Text));
        }
        return texts;
    }
}
