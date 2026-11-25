# Repository Guidelines

## Project Structure

TestReport is a .NET 10 Blazor app for comparing FastReport and Bold Reports PDF exports. `Program.cs` defines `/reports/{engine}/{rows}`. `ContractReport.cs` supplies contract data, renders both formats, and adds empty signature fields to Bold Reports PDFs. FastReport defines those fields in its template. The embedded templates live in `Reports/Fast/` and `Reports/Bold/`. Update the resource entries in `TestReport.csproj` when renaming a template. The comparison UI is in `Components/Pages/Home.razor`, with download logic in `wwwroot/report-export.js`.

## Build, Run, and Test

Run these from the repository root with the .NET 10 SDK. Start the web app before running either test script.

- `dotnet build TestReport.slnx` restores packages and builds the solution.
- `dotnet run --project TestReport.csproj --launch-profile http` starts the app at `http://localhost:5296`.
- `.\Test-Reports.ps1` checks both download paths, PDF pagination around the signing section, and two empty signature fields on the final page. It saves PDFs under `obj/report-check/`.
- `node Test-Reports-Load.cjs` benchmarks SQL-backed exports with 1, 5, and 10 concurrent requests. It writes PDFs and a CSV under `obj/sql-load/`.

The Development profile uses the `TestReportBenchmark` LocalDB instance for the home page and PDF exports. Run `Init-Benchmark.sql` on the test instance to seed it. Set `ConnectionStrings__Reports` to use a different test database. Use synthetic records and keep credentials out of project files.

## Coding and Verification

Use four-space indentation, PascalCase for C# types and methods, and camelCase for locals and JavaScript identifiers. Follow nearby Razor and JavaScript patterns. Keep dataset `Contracts` and field names aligned across the data provider and both templates. For layout changes, inspect both PDFs around the page boundary and confirm the signing section appears once, after the last row. Run the load script for performance changes and report row count, concurrency, and timing.

## Commits and Pull Requests

Git history is unavailable in this checkout. Use a short, imperative commit subject such as `Fix Bold signature pagination`. In pull requests, describe the behavior changed and the checks run. Link a related issue when available and attach a screenshot or synthetic PDF for visible layout changes.
