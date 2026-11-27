using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace TestReport.Features.ContractReportDesigner;

internal sealed record EditableReport(ReportObject[] Objects, ContractColumn[] Columns, int TableX, int TableY,
    int DataRows, TableMerge[] Merges, ReportTypography TableTypography);

internal static partial class ContractTemplate
{
    public const int MaxFrxBytes = 1024 * 1024;
    private const string UnsupportedFrx = "Mẫu .frx có cấu trúc hoặc thuộc tính mà trình thiết kế chưa hỗ trợ.";

    public static async Task<EditableReport> ImportAsync(Stream input)
    {
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxFrxBytes
        });
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, CancellationToken.None);
        var root = document.Root;
        if (root?.Name != "Report" || root.Element("ReportPage") is not { } page)
            throw new InvalidDataException(UnsupportedFrx);

        var canvas = page.Element("ReportTitleBand");
        var items = canvas?.Elements().Where(element => element.Name != "TableObject")
            .Select(ReadObject).ToArray() ?? [];
        var table = canvas?.Element("TableObject");
        var layout = table is null ? new EditableReport(items, [], 0, 0, 1, [], ReportTypography.TableDefault) : ReadTable(table, items);
        if (!layout.TableTypography.IsValid() ||
            !IsValid(layout.Columns, layout.TableX, layout.TableY, layout.DataRows, layout.TableTypography.Size) ||
            !MergesAreValid(layout.Columns, layout.DataRows, layout.Merges) ||
            items.Length > ReportObject.Limit || items.Select(item => item.Id).Distinct().Count() != items.Length ||
            items.Sum(item => (long)(item.Image?.Length ?? 0)) > ReportObject.ImagesLimit ||
            items.Any(item => !ReportObject.IsValid(item) || item.Image is { } image && !ReportObject.IsValidImage(image)))
            throw new InvalidDataException(UnsupportedFrx);

        // Round-trip through the editor schema so unsupported properties cannot disappear on save.
        using var regenerated = Create(items, layout.Columns, layout.TableX, layout.TableY, layout.DataRows,
            layout.Merges, layout.TableTypography);
        var generatedRoot = XDocument.Load(regenerated).Root!;
        if (!Matches(root, generatedRoot))
            throw new InvalidDataException(UnsupportedFrx);
        return layout;
    }

    private static EditableReport ReadTable(XElement table, ReportObject[] objects)
    {
        var rows = table.Elements("TableRow").ToArray();
        var columnElements = table.Elements("TableColumn").ToArray();
        var cells = rows.Select(row => row.Elements("TableCell").ToArray()).ToArray();
        if (rows.Length is < 2 or > MaxDataRows + 1 || columnElements.Length is < 1 or > MaxColumns ||
            cells.Any(row => row.Length != columnElements.Length))
            throw new InvalidDataException(UnsupportedFrx);

        var columns = columnElements.Select((column, index) => new ContractColumn(
            ReadId(column, "Column"), (string?)cells[0][index].Attribute("Text") ?? "",
            ReadInt(column, "Width"), (string?)cells[1][index].Attribute("HorzAlign") ?? "Left",
            ReadInt(cells[0][index], "ColSpan", 1))).ToArray();
        var merges = new List<TableMerge>();
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < columns.Length; column++)
            {
                var span = ReadInt(cells[row][column], "RowSpan", 1);
                if (span > 1)
                    merges.Add(new TableMerge(columns[column].Id, row, span));
            }
        }

        var typography = ReadTypography(cells[0][0], (bool?)rows[0].Attribute("AutoSize") ?? false);
        return new EditableReport(objects, columns, ReadInt(table, "Left"), ReadInt(table, "Top"), rows.Length - 1,
            merges.ToArray(), typography);
    }

    private static ReportObject ReadObject(XElement item)
    {
        var kind = item.Name.LocalName switch
        {
            "TextObject" => "text",
            "PictureObject" => "image",
            "ShapeObject" when (string?)item.Attribute("Shape") == "Ellipse" => "ellipse",
            "ShapeObject" => "rectangle",
            "LineObject" => "line",
            "DigitalSignatureObject" => "signature",
            _ => throw new InvalidDataException($"Mẫu .frx có đối tượng chưa hỗ trợ: {item.Name.LocalName}.")
        };
        return new ReportObject(ReadId(item, "Item"), kind, ReadInt(item, "Left"), ReadInt(item, "Top"),
            ReadInt(item, "Width"), kind == "line" ? 8 : ReadInt(item, "Height"),
            (string?)item.Attribute("Text") ?? "", (string?)item.Attribute("HorzAlign") ?? "Left",
            (string?)item.Attribute("Image"), kind == "text" ? ReadTypography(item, (bool?)item.Attribute("CanGrow") ?? false) : null);
    }

    private static ReportTypography ReadTypography(XElement element, bool autoSize)
    {
        var font = ((string?)element.Attribute("Font") ?? "").Split(", ");
        var rgb = ((string?)element.Attribute("TextFill.Color") ?? "").Split(", ");
        if (font.Length < 2 || !font[1].EndsWith("pt", StringComparison.Ordinal) || rgb.Length != 3 ||
            !int.TryParse(font[1].AsSpan(0, font[1].Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
            throw new InvalidDataException(UnsupportedFrx);

        var styles = font.Skip(2).Select(style => style.Replace("style=", "", StringComparison.Ordinal)).ToArray();
        var color = "#" + Convert.ToHexString(rgb.Select(value => byte.Parse(value, CultureInfo.InvariantCulture)).ToArray());
        return new ReportTypography(font[0], size, styles.Contains("Bold"), styles.Contains("Italic"),
            styles.Contains("Underline"), color, (bool?)element.Attribute("WordWrap") ?? true, autoSize);
    }

    private static int ReadId(XElement element, string prefix)
    {
        var name = (string?)element.Attribute("Name");
        if (name is null || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
            id is < 1 or > int.MaxValue - 1000)
            throw new InvalidDataException(UnsupportedFrx);
        return id;
    }

    private static int ReadInt(XElement element, string attribute, int fallback = 0) =>
        (int?)element.Attribute(attribute) ?? fallback;

    private static bool Matches(XElement actual, XElement expected) =>
        actual.Name == expected.Name && actual.Attributes().Count() == expected.Attributes().Count() &&
        expected.Attributes().All(attribute => (string?)actual.Attribute(attribute.Name) == attribute.Value) &&
        actual.Nodes().Count() == expected.Nodes().Count() &&
        actual.Nodes().Zip(expected.Nodes()).All(pair => pair is (XElement left, XElement right) && Matches(left, right));
}
