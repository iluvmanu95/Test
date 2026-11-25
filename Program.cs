using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Globalization;
using Syncfusion.Pdf.Parsing;
using TestReport;
using TestReport.Components;

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

app.MapGet("/reports/{engine}/{rows:int}", async (string engine, int rows, HttpContext context) =>
{
    if (rows is < 1 or > 200)
        return Results.BadRequest("Chọn số dòng từ 1 đến 200.");
    if (engine is not ("fast" or "bold"))
        return Results.NotFound();

    var dataTimer = Stopwatch.StartNew();
    var data = string.IsNullOrWhiteSpace(reportsConnection)
        ? ContractReport.Sample(rows)
        : await ContractReport.ReadSqlAsync(reportsConnection, rows, context.RequestAborted);
    if (data.Rows.Count != rows)
        return Results.BadRequest("Cơ sở dữ liệu thử thiếu dữ liệu hợp đồng.");
    dataTimer.Stop();
    var timer = Stopwatch.StartNew();
    var pdf = engine == "fast" ? ContractReport.Fast(data) : ContractReport.Bold(data);
    var engineMilliseconds = timer.Elapsed.TotalMilliseconds;
    if (engine == "bold")
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
    return Results.File(pdf, "application/pdf", $"{engine}-hop-dong-{rows}.pdf");
});

app.Run();
