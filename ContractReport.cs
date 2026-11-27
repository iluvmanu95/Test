using System.Data;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using BoldReports.Writer;
using FastReport;
using FastReport.Export.Pdf;
using Syncfusion.Drawing;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;

namespace TestReport;

internal static class ContractReport
{
    public const int MaxRows = 2000;
    private static readonly string[] Roles = ["Người lập biểu", "Người phê duyệt"];
    private static readonly string[] Columns =
    [
        "SoThuTu", "MaNhanVien", "HoTen", "PhongBan", "SoHopDong", "LoaiHopDong",
        "NgayHieuLuc", "NgayHetHan", "MucLuong"
    ];
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    // Tạo dữ liệu hợp đồng giả trong bộ nhớ. Khi chuyển sang SQL Server, giữ tên bảng và cột mà hai mẫu báo cáo dùng.
    public static DataTable Sample(int count)
    {
        var table = new DataTable("Contracts");
        foreach (var column in Columns)
            table.Columns.Add(column, typeof(string));

        string[] names = ["Nguyễn Thị Minh Anh", "Trần Quốc Bảo", "Lê Hoàng Yến", "Phạm Đình Khôi", "Đỗ Ngọc Ánh"];
        string[] offices = ["Hành chính – Nhân sự", "Kế toán", "Vận hành", "Quản lý chất lượng và Đào tạo nội bộ"];
        for (var number = 1; number <= count; number++)
        {
            var date = new DateOnly(2026, 11, 1).AddDays(number - 1);
            var permanent = number % 3 == 0;
            table.Rows.Add(number.ToString(), $"NV{number:000}", names[(number - 1) % names.Length],
                offices[(number - 1) % offices.Length], $"HĐLĐ-{number:000}/2026",
                permanent ? "Không xác định thời hạn" : "Xác định thời hạn",
                date.ToString("dd/MM/yyyy"), permanent ? null : date.AddYears(1).AddDays(-1).ToString("dd/MM/yyyy"),
                (18_000_000 + number * 500_000).ToString("N0", Vietnamese));
        }
        return table;
    }

    public static async Task<DataTable> ReadSqlAsync(string connectionString, int count, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT TOP (@count) CONVERT(nvarchar(10), SoThuTu) AS SoThuTu,
                MaNhanVien, HoTen, PhongBan, SoHopDong, LoaiHopDong,
                CONVERT(nvarchar(10), NgayHieuLuc, 103) AS NgayHieuLuc,
                CONVERT(nvarchar(10), NgayHetHan, 103) AS NgayHetHan, MucLuong
            FROM dbo.Contracts
            ORDER BY dbo.Contracts.SoThuTu
            """, connection);
        command.Parameters.Add("@count", SqlDbType.Int).Value = count;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var table = new DataTable("Contracts");
        foreach (var column in Columns)
            table.Columns.Add(column, typeof(string));

        var values = new object[Columns.Length];
        while (await reader.ReadAsync(cancellationToken))
        {
            reader.GetValues(values);
            values[^1] = ((decimal)values[^1]).ToString("N0", Vietnamese);
            table.Rows.Add(values);
        }


        return table;
    }

    public static byte[] Fast(DataTable data, string layout)
    {
        using var input = typeof(ContractReport).Assembly.GetManifestResourceStream($"TestReport.Fast.{layout}.frx")
            ?? throw new InvalidOperationException("Không tìm thấy mẫu báo cáo FastReport.");
        return Fast(input, data);
    }

    public static byte[] Fast(Stream input, DataTable? data = null)
    {
        using var report = new Report();
        report.Load(input);
        if (data is not null)
            report.RegisterData(data, data.TableName);
        else if (report.FindObject("Data1") is DataBand band)
        {
            // The Designer left an Employees binding on the static contract body.
            band.DataSource = null;
            band.Sort.Clear();
            band.RowCount = 1;
        }
        report.Prepare();
        using var output = new MemoryStream();
        using var export = new PDFExport { InteractiveForms = true, EmbeddingFonts = true, IsDigitalSignEnable = true };
        report.Export(export, output);
        return output.ToArray();
    }

    public static byte[] FastDesigned(Stream input, DataTable data)
    {
        var template = XDocument.Load(input);
        var page = template.Root?.Element("ReportPage");
        var title = page?.Element("ReportTitleBand");
        var table = title?.Element("TableObject");
        var rows = table?.Elements("TableRow").ToArray();
        if (page is null || title is null || table is null || rows is not { Length: 2 } ||
            table.Elements("TableColumn").Count() != Columns.Length ||
            rows[1].Elements("TableCell").Count() != Columns.Length ||
            rows[1].Elements("TableCell").Any(cell => (int?)cell.Attribute("RowSpan") > 1 || (int?)cell.Attribute("ColSpan") > 1))
            throw new InvalidDataException("Mẫu in cần một bảng có một hàng tiêu đề và một hàng dữ liệu gồm 9 ô riêng.");

        var tableTop = (decimal?)table.Attribute("Top") ?? 0;
        var rowHeight = (decimal?)rows[1].Attribute("Height") ?? 24;
        var tableHeight = (decimal?)table.Attribute("Height") ?? 54;
        var tableBottom = tableTop + tableHeight;
        var titleHeight = (decimal?)title.Attribute("Height") ?? tableBottom;
        var headerHeight = (decimal?)rows[0].Attribute("Height") ?? 30;

        var header = new XElement(table);
        header.Elements("TableRow").Last().Remove();
        header.SetAttributeValue("Height", headerHeight);
        header.SetAttributeValue("Top", 0);
        foreach (var name in header.DescendantsAndSelf().Attributes("Name"))
            name.Value = "PrintHeader" + name.Value;
        var dataHeader = new XElement("DataHeaderBand", new XAttribute("Name", "PrintHeaders"),
            new XAttribute("Height", headerHeight), new XAttribute("CanGrow", true),
            new XAttribute("RepeatOnEveryPage", true), header);

        table.Remove();
        rows[0].Remove();
        table.SetAttributeValue("Top", 0);
        table.SetAttributeValue("Height", rowHeight);
        table.SetAttributeValue("CanGrow", true);
        rows[1].SetAttributeValue("AutoSize", true);
        rows[1].SetAttributeValue("MinHeight", rowHeight);
        foreach (var (cell, column) in rows[1].Elements("TableCell").Zip(Columns))
        {
            cell.SetAttributeValue("Text", $"[{data.TableName}.{column}]");
            cell.SetAttributeValue("WordWrap", true);
            cell.SetAttributeValue("AllowExpressions", true);
        }

        var detail = new XElement("DataBand", new XAttribute("Name", "PrintDetails"),
            new XAttribute("DataSource", data.TableName), new XAttribute("Height", rowHeight),
            new XAttribute("CanGrow", true), dataHeader, table);
        title.SetAttributeValue("Height", tableTop);
        title.AddAfterSelf(detail);

        var belowTable = title.Elements().Where(item => ((decimal?)item.Attribute("Top") ?? 0) >= tableBottom).ToArray();
        if (belowTable.Length > 0)
        {
            var summary = new XElement("ReportSummaryBand", new XAttribute("Name", "PrintFooter"),
                new XAttribute("Height", Math.Max(0, titleHeight - tableBottom)));
            foreach (var item in belowTable)
            {
                item.Remove();
                item.SetAttributeValue("Top", (decimal)item.Attribute("Top")! - tableBottom);
                summary.Add(item);
            }
            detail.AddAfterSelf(summary);
        }

        var dictionary = new XElement("Dictionary", new XElement("TableDataSource",
            new XAttribute("Name", data.TableName), new XAttribute("ReferenceName", data.TableName),
            new XAttribute("DataType", "System.Int32"), new XAttribute("Enabled", true),
            Columns.Select(column => new XElement("Column", new XAttribute("Name", column),
                new XAttribute("DataType", "System.String")))));
        template.Root!.AddFirst(dictionary);

        using var adapted = new MemoryStream();
        template.Save(adapted);
        adapted.Position = 0;
        return Fast(adapted, data);
    }

    public static byte[] FastDemo(Stream input, DataTable data)
    {
        var template = XDocument.Load(input);
        var band = template.Descendants("DataBand").Single();
        var table = band.Element("TableObject")!;
        var rows = table.Elements("TableRow").ToArray();
        var cells = rows[1].Elements("TableCell").ToArray();
        if (cells.Length != Columns.Length)
            throw new InvalidOperationException("Mẫu test demo phải có đủ 9 cột hợp đồng.");

        var source = template.Descendants("TableDataSource").Single();
        source.SetAttributeValue("Name", data.TableName);
        source.SetAttributeValue("ReferenceName", data.TableName);
        source.ReplaceNodes(Columns.Select(column => new XElement("Column",
            new XAttribute("Name", column), new XAttribute("DataType", "System.String"))));
        band.SetAttributeValue("DataSource", data.TableName);
        band.SetAttributeValue("CanGrow", "true");
        band.SetAttributeValue("Height", "22");
        band.Elements("Sort").Remove();
        band.Elements("ChildBand").Remove();

        var title = band.Parent!.Element("ReportTitleBand")!;
        var header = new XElement(table);
        header.SetAttributeValue("Name", "ContractHeaders");
        header.SetAttributeValue("Top", title.Attribute("Height")!.Value);
        header.Elements("TableRow").Skip(1).Remove();
        header.Element("TableRow")!.SetAttributeValue("AutoSize", "true");
        foreach (var name in header.Descendants().Attributes("Name"))
            name.Value = "Header" + name.Value;
        title.SetAttributeValue("CanGrow", "true");
        title.Add(header);

        rows.Where(row => row != rows[1]).Remove();
        table.SetAttributeValue("Top", "0");
        table.SetAttributeValue("Height", "22");
        for (var i = 0; i < cells.Length; i++)
            cells[i].SetAttributeValue("Text", $"[{data.TableName}.{Columns[i]}]");

        var signatures = band.Elements("DigitalSignatureObject").ToArray();
        var summary = new XElement("ReportSummaryBand", new XAttribute("Name", "Signing"),
            new XAttribute("Width", band.Attribute("Width")!.Value), new XAttribute("Height", "58"));
        foreach (var signature in signatures)
        {
            signature.Remove();
            signature.SetAttributeValue("Top", "8");
            summary.Add(signature);
        }
        band.AddAfterSelf(summary);

        using var adapted = new MemoryStream();
        template.Save(adapted);
        adapted.Position = 0;
        return Fast(adapted, data);
    }

    public static byte[] Bold(DataTable data, string layout)
    {
        using var input = typeof(ContractReport).Assembly.GetManifestResourceStream($"TestReport.Bold.{layout}.rdl")
            ?? throw new InvalidOperationException("Không tìm thấy mẫu báo cáo Bold Reports.");
        using var output = new MemoryStream();
        var writer = new ReportWriter();
        writer.LoadReport(input);
        writer.DataSources.Add(new BoldReports.Web.ReportDataSource { Name = "Contracts", Value = data });
        writer.Save(output, WriterFormat.PDF);
        return output.ToArray();
    }

    public static byte[] AddBoldSignatureFields(byte[] pdf, string lastEmployeeCode)
    {
        using var document = new PdfLoadedDocument(pdf);
        var pageIndex = document.Pages.Count - 1;
        var page = document.Pages[pageIndex];
        // shortcut: relies on the unique, searchable employee code in this test report; use a layout anchor for other report templates.
        if (!document.FindText(lastEmployeeCode, pageIndex, out List<RectangleF> matches) || matches.Count != 1)
            throw new InvalidOperationException("Không tìm thấy dòng cuối của báo cáo Bold Reports.");

        const float margin = 28.35f;
        const float fieldWidth = 180;
        const float fieldHeight = 52.5f;
        const float labelHeight = 20;
        var top = matches[0].Bottom + 16;
        if (top + labelHeight + fieldHeight > page.Size.Height - margin)
        {
            page = document.Pages.Add(page.Size, new PdfMargins { All = 0 });
            top = margin;
        }

        if (document.Form is null)
            document.CreateForm();
        var form = document.Form ?? throw new InvalidOperationException("Không tạo được biểu mẫu PDF.");
        using var fontFile = File.OpenRead(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"));
        var font = new PdfTrueTypeFont(fontFile, 9);
        var centered = new PdfStringFormat(PdfTextAlignment.Center, PdfVerticalAlignment.Middle);
        for (var i = 0; i < Roles.Length; i++)
        {
            var left = margin + (62 + i * 328) * 0.75f;
            page.Graphics.DrawString(Roles[i], font, PdfBrushes.Black,
                new RectangleF(left, top, fieldWidth, labelHeight), centered);
            var bounds = new RectangleF(left, top + labelHeight, fieldWidth, fieldHeight);
            page.Graphics.DrawRectangle(PdfPens.Gray, bounds);
            form.Fields.Add(new PdfSignatureField(page, $"Signature{i + 1}") { Bounds = bounds });
        }

        var auxiliaryBounds = new RectangleF(margin + 646 * 0.75f, top + labelHeight + 10.5f, 73.5f, 42);
        page.Graphics.DrawRectangle(PdfPens.Gray, auxiliaryBounds);
        form.Fields.Add(new PdfSignatureField(page, "Signature3") { Bounds = auxiliaryBounds });

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
