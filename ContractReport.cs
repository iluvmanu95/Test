using System.Data;
using System.Globalization;
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

    public static byte[] Fast(DataTable data)
    {
        using var report = new Report();
        using var input = typeof(ContractReport).Assembly.GetManifestResourceStream("TestReport.Fast.ContractSummary.frx")
            ?? throw new InvalidOperationException("Không tìm thấy mẫu báo cáo FastReport.");
        report.Load(input);
        report.RegisterData(data, data.TableName);
        report.Prepare();
        using var output = new MemoryStream();
        using var export = new PDFExport { InteractiveForms = true, EmbeddingFonts = true, IsDigitalSignEnable = true };
        report.Export(export, output);
        return output.ToArray();
    }

    public static byte[] Bold(DataTable data)
    {
        using var input = typeof(ContractReport).Assembly.GetManifestResourceStream("TestReport.Bold.ContractSummary.rdl")
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
            var left = margin + (62 + i * 410) * 0.75f;
            page.Graphics.DrawString(Roles[i], font, PdfBrushes.Black,
                new RectangleF(left, top, fieldWidth, labelHeight), centered);
            var bounds = new RectangleF(left, top + labelHeight, fieldWidth, fieldHeight);
            page.Graphics.DrawRectangle(PdfPens.Gray, bounds);
            form.Fields.Add(new PdfSignatureField(page, $"Signature{i + 1}") { Bounds = bounds });
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
