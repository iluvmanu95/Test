using System.Text.Json.Serialization;
using System.Xml.Linq;
using SkiaSharp;

namespace TestReport.Features.ContractReportDesigner;

internal sealed record ReportObject(int Id, string Kind, int X, int Y, int Width, int Height,
    string Text, string Align, string? Image = null, ReportTypography? Typography = null)
{
    public const int CanvasHeight = 680;
    public const int Limit = 48;
    public const int MaxImageBytes = 128 * 1024;
    public const int ImagesLimit = 512 * 1024;
    [JsonIgnore]
    public ReportTypography TextStyle => Typography ?? ReportTypography.Default;

    public static readonly (string Kind, string Label, int Width, int Height)[] Tools =
    [
        ("text", "Văn bản", 200, 48),
        ("image", "Ảnh", 200, 120),
        ("rectangle", "Hình chữ nhật", 160, 80),
        ("ellipse", "Hình elip", 130, 72),
        ("line", "Đường kẻ", 180, 8),
        ("signature", "Ô ký số", 220, 72)
    ];

    public static bool IsValid(ReportObject item) =>
        item is { Id: > 0, X: >= 0, Y: >= 0, Width: >= 12 and <= ContractTemplate.PageWidth,
            Height: >= 8 and <= CanvasHeight } &&
        item.X <= ContractTemplate.PageWidth - item.Width && item.Y <= CanvasHeight - item.Height &&
        Tools.Any(tool => tool.Kind == item.Kind) &&
        item.Align is "Left" or "Center" or "Right" &&
        ContractTemplate.IsValidText(item.Text, 200) && item.TextStyle.IsValid();

    public static bool IsValidImage(string image)
    {
        if (image.Length > (MaxImageBytes + 2) / 3 * 4)
            return false;
        var bytes = new byte[MaxImageBytes];
        if (!Convert.TryFromBase64String(image, bytes, out var size))
            return false;
        using var data = SKData.CreateCopy(bytes.AsSpan(0, size).ToArray());
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg) ||
            codec.Info.Width is < 1 or > 2048 || codec.Info.Height is < 1 or > 2048)
            return false;
        using var bitmap = new SKBitmap(codec.Info);
        return codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success;
    }

    public XElement ToFrx()
    {
        var type = Kind switch
        {
            "text" => "TextObject",
            "line" => "LineObject",
            "signature" => "DigitalSignatureObject",
            "image" => "PictureObject",
            _ => "ShapeObject"
        };
        var element = new XElement(type, new XAttribute("Name", $"Item{Id}"),
            new XAttribute("Left", X), new XAttribute("Top", Y), new XAttribute("Width", Width),
            new XAttribute("Height", Kind == "line" ? 0 : Height));
        if (Kind == "text")
            element.Add(new XAttribute("Text", Text), new XAttribute("AllowExpressions", false),
                new XAttribute("HorzAlign", Align), new XAttribute("CanGrow", TextStyle.AutoSize), TextStyle.Frx());
        else if (Kind == "ellipse")
            element.Add(new XAttribute("Shape", "Ellipse"));
        else if (Kind == "signature")
            element.Add(new XAttribute("Border.Lines", "All"));
        else if (Kind == "image" && Image is not null)
            element.Add(new XAttribute("Image", Image), new XAttribute("SizeMode", "Zoom"));
        return element;
    }
}
