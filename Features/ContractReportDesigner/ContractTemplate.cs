using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;

namespace TestReport.Features.ContractReportDesigner;

internal sealed record ContractColumn(int Id, string Label, int Width, string Align, int HeaderSpan = 1)
{
    [JsonIgnore]
    public string Key => $"Column{Id}";
}

internal sealed record TableMerge(int ColumnId, int Row, int Span)
{
    public bool Covers(int columnId, int row) => ColumnId == columnId && row >= Row && row < Row + Span;
}

internal static partial class ContractTemplate
{
    public const int PageWidth = 1040;
    public const int MinColumnWidth = 36;
    public const int MaxColumns = 16;
    public const int MaxDataRows = 12;
    public static int RowHeight(int size, bool header = false) =>
        Math.Max(header ? 30 : 24, (size * 4 + 2) / 3 + 8);

    public static int TableHeight(int dataRows, int size) => RowHeight(size, true) + dataRows * RowHeight(size);

    public static IResult Export([FromForm] string output, [FromForm] string columns, [FromForm] string? objects,
        [FromForm] int? tableX, [FromForm] int? tableY,
        [FromForm] int? dataRows, [FromForm] string? merges, [FromForm] string? tableTypography, HttpContext context)
    {
        if (output is not ("frx" or "pdf") || columns.Length > 7000 || objects?.Length > 600000 ||
            merges?.Length > 10000 || tableTypography?.Length > 1000)
            return Results.BadRequest("Thông tin mẫu báo cáo không hợp lệ.");

        ContractColumn[]? layout;
        ReportObject[]? items;
        TableMerge[]? vertical;
        ReportTypography? typography;
        try
        {
            layout = JsonSerializer.Deserialize<ContractColumn[]>(columns);
            items = objects is null ? [] : JsonSerializer.Deserialize<ReportObject[]>(objects);
            vertical = merges is null ? [] : JsonSerializer.Deserialize<TableMerge[]>(merges);
            typography = tableTypography is null ? ReportTypography.TableDefault : JsonSerializer.Deserialize<ReportTypography>(tableTypography);
        }
        catch (JsonException)
        {
            return Results.BadRequest("Bố cục báo cáo không hợp lệ.");
        }

        var x = tableX ?? 0;
        var y = tableY ?? 0;
        var rows = dataRows ?? 1;
        if (typography is null || !typography.IsValid() ||
            layout is null || !IsValid(layout, x, y, rows, typography.Size) ||
            vertical is null || !MergesAreValid(layout, rows, vertical) ||
            items is null || items.Length > ReportObject.Limit ||
            items.Sum(item => item?.Image?.Length ?? 0) > ReportObject.ImagesLimit ||
            items.Any(item => !ReportObject.IsValid(item)) || items.Select(item => item.Id).Distinct().Count() != items.Length)
            return Results.BadRequest("Kiểm tra thuộc tính, kích thước và vị trí của các đối tượng.");

        if (items.Any(item => item.Image is { } image && (item.Kind != "image" || !ReportObject.IsValidImage(image))))
            return Results.BadRequest("Ảnh PNG hoặc JPEG không hợp lệ.");

        using var template = Create(items, layout, x, y, rows, vertical, typography);
        context.Response.Headers.CacheControl = "no-store";
        if (output == "frx")
            return Results.File(template.ToArray(), "application/xml; charset=utf-8", "bao-cao.frx");

        return Results.File(ContractReport.Fast(template), "application/pdf");
    }

    public static bool IsValid(IReadOnlyList<ContractColumn> columns, int x, int y, int rows, int size) =>
        columns.Count <= MaxColumns &&
        rows is >= 1 and <= MaxDataRows &&
        x is >= 0 and <= PageWidth && y >= 0 && y <= ReportObject.CanvasHeight - TableHeight(rows, size) &&
        columns.All(column => column is { Id: > 0 } &&
            !string.IsNullOrWhiteSpace(column.Label) && IsValidText(column.Label, 40) &&
            column.Align is "Left" or "Center" or "Right" && column.Width is >= MinColumnWidth and <= PageWidth) &&
        columns.Select(column => column.Id).Distinct().Count() == columns.Count &&
        HasValidHeaderSpans(columns) &&
        columns.Sum(column => column.Width) <= PageWidth - x;

    private static bool MergesAreValid(ContractColumn[] columns, int rows, TableMerge[] merges)
    {
        var cells = new HashSet<(int ColumnId, int Row)>();
        foreach (var merge in merges)
        {
            if (merge is not { Row: >= 0, Span: >= 2 } || merge.Row > rows || merge.Span > rows + 1 - merge.Row)
                return false;
            var index = Array.FindIndex(columns, column => column.Id == merge.ColumnId);
            if (index < 0 || (merge.Row == 0 &&
                columns.Take(index + 1).Where((_, start) => start + columns[start].HeaderSpan > index)
                    .Any(column => column.HeaderSpan > 1)))
                return false;
            for (var row = merge.Row; row < merge.Row + merge.Span; row++)
                if (!cells.Add((merge.ColumnId, row)))
                    return false;
        }
        return true;
    }

    private static bool HasValidHeaderSpans(IReadOnlyList<ContractColumn> columns)
    {
        var end = 0;
        for (var index = 0; index < columns.Count; index++)
        {
            var span = columns[index].HeaderSpan;
            if (span < 1 || span > columns.Count - index || (index < end && span != 1))
                return false;
            if (index >= end)
                end = index + span;
        }
        return true;
    }

    internal static bool IsValidText(string? value, int maxLength)
    {
        if (value is null || value.Length > maxLength)
            return false;
        try
        {
            XmlConvert.VerifyXmlChars(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static MemoryStream Create(IReadOnlyList<ReportObject> objects, IReadOnlyList<ContractColumn> columns,
        int x, int y, int rows, IReadOnlyList<TableMerge> merges, ReportTypography typography)
    {
        var page = new XElement("ReportPage", new XAttribute("Name", "Page1"),
            new XAttribute("Landscape", true), new XAttribute("PaperWidth", 297), new XAttribute("PaperHeight", 210));
        var template = new XElement("Report", page);
        if (objects.Count > 0 || columns.Count > 0)
        {
            var bottom = objects.Select(item => item.Y + item.Height).DefaultIfEmpty(0).Max();
            var canvas = new XElement("ReportTitleBand", new XAttribute("Name", "Canvas"),
                new XAttribute("Height", columns.Count > 0 ? Math.Max(bottom, y + TableHeight(rows, typography.Size)) : bottom),
                new XAttribute("CanGrow", typography.AutoSize || objects.Any(item => item.Kind == "text" && item.TextStyle.AutoSize)),
                objects.Select(item => item.ToFrx()));
            if (columns.Count > 0)
                canvas.Add(BuildTable(columns, x, y, rows, merges, typography));
            page.Add(canvas);
        }

        var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
            template.Save(writer);
        stream.Position = 0;
        return stream;
    }

    private static XElement BuildTable(IReadOnlyList<ContractColumn> columns, int x, int y, int rows,
        IReadOnlyList<TableMerge> merges, ReportTypography typography) =>
        new("TableObject", new XAttribute("Name", "ReportTable"), new XAttribute("Left", x), new XAttribute("Top", y),
            new XAttribute("Width", columns.Sum(column => column.Width)), new XAttribute("Height", TableHeight(rows, typography.Size)),
            new XAttribute("CanGrow", typography.AutoSize),
            columns.Select(column => new XElement("TableColumn", new XAttribute("Name", column.Key),
                new XAttribute("Width", column.Width))),
            Enumerable.Range(0, rows + 1).Select(row => new XElement("TableRow",
                new XAttribute("Name", $"Row{row}"), new XAttribute("Height", RowHeight(typography.Size, row == 0)),
                new XAttribute("AutoSize", typography.AutoSize),
                typography.AutoSize ? new XAttribute("MinHeight", RowHeight(typography.Size, row == 0)) : null,
                columns.Select(column => Cell($"Cell{row}{column.Key}", row == 0 ? column.Label : "",
                    row == 0 ? "Center" : column.Align, row == 0, row == 0 ? column.HeaderSpan : 1,
                    merges.FirstOrDefault(merge => merge.ColumnId == column.Id && merge.Row == row)?.Span ?? 1,
                    typography)))));

    private static XElement Cell(string name, string text, string alignment, bool header, int span, int rowSpan, ReportTypography typography) =>
        new("TableCell", new XAttribute("Name", name), new XAttribute("Text", text),
            new XAttribute("HorzAlign", alignment), new XAttribute("VertAlign", "Center"),
            typography.Frx(), new XAttribute("Border.Lines", "All"),
            new XAttribute("AllowExpressions", !header),
            span > 1 ? new XAttribute("ColSpan", span) : null,
            rowSpan > 1 ? new XAttribute("RowSpan", rowSpan) : null,
            header ? new XAttribute("Fill.Color", "Gainsboro") : null);
}
