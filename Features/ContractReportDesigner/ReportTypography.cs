using System.Xml.Linq;

namespace TestReport.Features.ContractReportDesigner;

public sealed record ReportTypography(string Font = "Segoe UI", int Size = 12, bool Bold = false,
    bool Italic = false, bool Underline = false, string Color = "#000000", bool WordWrap = true, bool AutoSize = false)
{
    public static readonly ReportTypography Default = new();
    public static readonly ReportTypography TableDefault = new(Size: 9);
    public static readonly string[] Fonts = ["Segoe UI", "Arial", "Calibri", "Times New Roman", "Verdana", "Courier New"];

    public bool IsValid() =>
        Fonts.Contains(Font) && Size is >= 8 and <= 48 &&
        Color is { Length: 7 } && Color[0] == '#' && Color.Skip(1).All(Uri.IsHexDigit);

    public string Css() =>
        $"font-family:'{Font}';font-size:{Size}pt;font-weight:{(Bold ? 700 : 400)};" +
        $"font-style:{(Italic ? "italic" : "normal")};text-decoration:{(Underline ? "underline" : "none")};" +
        $"color:{Color};white-space:{(WordWrap ? "pre-wrap" : "pre")}";

    public XAttribute[] Frx()
    {
        var styles = new List<string>();
        if (Bold)
            styles.Add("Bold");
        if (Italic)
            styles.Add("Italic");
        if (Underline)
            styles.Add("Underline");
        var style = styles.Count == 0 ? "" : ", style=" + string.Join(", ", styles);
        var rgb = Convert.FromHexString(Color[1..]);
        return [new("Font", $"{Font}, {Size}pt{style}"), new("TextFill.Color", $"{rgb[0]}, {rgb[1]}, {rgb[2]}"),
            new("WordWrap", WordWrap)];
    }
}
