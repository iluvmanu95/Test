using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Globalization;
using Syncfusion.Pdf.Parsing;
using TestReport;
using TestReport.Components;
using TestReport.Features.ContractReportDesigner;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
var reportsConnection = app.Configuration.GetConnectionString("Reports");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/reports/{engine}/{rows:int}", async (string engine, int rows, string? template, HttpContext context) =>
{
    if (rows is < 1 or > ContractReport.MaxRows)
        return Results.BadRequest($"Chọn số dòng từ 1 đến {ContractReport.MaxRows}.");
    if (engine is not ("fast" or "bold"))
        return Results.NotFound();
    var layout = template switch
    {
        null or "summary" => "ContractSummary",
        "list" => "ContractList",
        "test-demo" => "TestDemo",
        _ => null
    };
    if (layout is null)
        return Results.BadRequest("Mẫu báo cáo không hợp lệ.");
    var fastDemo = engine == "fast" && template == "test-demo";
    var demoPath = Path.Combine(app.Environment.ContentRootPath, "Reports", "Fast", "test demo.frx");
    if (fastDemo && !File.Exists(demoPath))
        return Results.NotFound("Không tìm thấy file test demo.frx.");
    if (!fastDemo)
    {
        var resource = engine == "fast" ? $"TestReport.Fast.{layout}.frx" : $"TestReport.Bold.{layout}.rdl";
        using var input = typeof(ContractReport).Assembly.GetManifestResourceStream(resource);
        if (input is null)
            return Results.NotFound("Không tìm thấy file mẫu báo cáo.");
    }

    var dataTimer = Stopwatch.StartNew();
    var data = string.IsNullOrWhiteSpace(reportsConnection)
        ? ContractReport.Sample(rows)
        : await ContractReport.ReadSqlAsync(reportsConnection, rows, context.RequestAborted);
    if (data.Rows.Count != rows)
        return Results.BadRequest("Cơ sở dữ liệu thử thiếu dữ liệu hợp đồng.");
    dataTimer.Stop();
    var timer = Stopwatch.StartNew();
    byte[] pdf;
    if (fastDemo)
    {
        using var input = File.OpenRead(demoPath);
        pdf = ContractReport.FastDemo(input, data);
    }
    else
    {
        pdf = engine == "fast" ? ContractReport.Fast(data, layout) : ContractReport.Bold(data, layout);
    }
    var engineMilliseconds = timer.Elapsed.TotalMilliseconds;
    if (engine == "bold" && template != "test-demo")
        pdf = ContractReport.AddBoldSignatureFields(pdf, (string)data.Rows[rows - 1]["MaNhanVien"]);
    timer.Stop();

    using var document = new PdfLoadedDocument(pdf);
    var fields = document.Form?.Fields;
    var emptySignatures = 0;
    for (var i = 0; i < fields?.Count; i++)
        if (fields[i] is PdfLoadedSignatureField { IsSigned: false } field && ReferenceEquals(field.Page, document.Pages[document.Pages.Count - 1]))
            emptySignatures++;

    context.Response.Headers["X-Engine-Ms"] = engineMilliseconds.ToString("F2", CultureInfo.InvariantCulture);
    context.Response.Headers["X-Data-Ms"] = dataTimer.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture);
    context.Response.Headers["X-Data-Source"] = string.IsNullOrWhiteSpace(reportsConnection) ? "sample" : "sql";
    context.Response.Headers["X-Export-Ms"] = timer.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture);
    context.Response.Headers["X-Pdf-Pages"] = document.Pages.Count.ToString();
    context.Response.Headers["X-Pdf-Empty-Signatures"] = emptySignatures.ToString();
    context.Response.Headers.CacheControl = "no-store";
    var fileLabel = template switch
    {
        "test-demo" => "test-demo",
        "list" => "danh-sach-hop-dong",
        _ => "hop-dong"
    };
    return Results.File(pdf, "application/pdf", $"{engine}-{fileLabel}-{rows}.pdf");
});

app.MapGet("/reports/sample", (string template, IWebHostEnvironment environment, HttpContext context) =>
{
    if (template is not ("hdthuviec" or "hopdong"))
        return Results.NotFound();

    var path = Path.Combine(environment.ContentRootPath, "Reports", "Fast", $"{template}.frx");
    using var input = File.Exists(path) ? File.OpenRead(path) : null;
    if (input is null)
        return Results.NotFound("Không tìm thấy file mẫu FastReport.");

    var pdf = ContractReport.Fast(input);
    context.Response.Headers.CacheControl = "no-store";
    return Results.File(pdf, "application/pdf", $"{template}.pdf");
});

app.MapGet("/reports/print/{rows:int}", async (int rows, HttpContext context) =>
{
    if (rows is < 1 or > ContractReport.MaxRows)
        return Results.BadRequest($"Chọn số dòng từ 1 đến {ContractReport.MaxRows}.");

    var path = Path.Combine(app.Environment.ContentRootPath, "Reports", "bao-cao.frx");
    if (!File.Exists(path))
        return Results.NotFound("Không tìm thấy Reports/bao-cao.frx.");

    var data = string.IsNullOrWhiteSpace(reportsConnection)
        ? ContractReport.Sample(rows)
        : await ContractReport.ReadSqlAsync(reportsConnection, rows, context.RequestAborted);
    if (data.Rows.Count == 0)
        return Results.BadRequest("Không có hợp đồng để in.");

    using var input = File.OpenRead(path);
    try
    {
        var pdf = ContractReport.FastDesigned(input, data);
        context.Response.Headers.CacheControl = "no-store";
        return Results.File(pdf, "application/pdf", $"bao-cao-{data.Rows.Count}.pdf");
    }
    catch (InvalidDataException error)
    {
        return Results.BadRequest(error.Message);
    }
});

app.MapPost("/contract-report-designer/export", ContractTemplate.Export);

app.Run();
