param([string]$BaseUrl = 'http://localhost:5296')
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$outDir = Join-Path $PSScriptRoot 'obj/report-check'
New-Item -ItemType Directory -Force $outDir | Out-Null

$pageResponse = Invoke-WebRequest -UseBasicParsing $BaseUrl
if ($pageResponse.Content -notmatch 'data-engine="fast"' -or $pageResponse.Content -notmatch 'data-engine="bold"') {
    throw 'Missing report download controls.'
}

foreach ($engine in 'fast', 'bold') {
    $response = Invoke-WebRequest -UseBasicParsing "$BaseUrl/reports/$engine/5?template=test-demo" -OutFile (Join-Path $outDir "$engine-test-demo-5.pdf") -PassThru
    if ($response.Headers['Content-Disposition'] -notmatch 'test-demo' -or
        $response.Headers['X-Pdf-Empty-Signatures'] -ne '3') {
        throw "Invalid test demo report: $engine."
    }
}

foreach ($rows in 5, 200, 2000) {
    foreach ($engine in 'fast', 'bold') {
        $response = Invoke-WebRequest -UseBasicParsing "$BaseUrl/reports/$engine/${rows}?template=list" -OutFile (Join-Path $outDir "$engine-list-$rows.pdf") -PassThru
        if ($response.Headers['Content-Disposition'] -notmatch 'danh-sach-hop-dong' -or
            $response.Headers['X-Pdf-Empty-Signatures'] -ne '3') {
            throw "Invalid list report: $engine, $rows rows."
        }
    }
}

foreach ($rows in 5, 17, 18, 19, 23, 60, 200, 2000) {
    $timings = @{ fast = @(); bold = @() }
    for ($run = 0; $run -lt 6; $run++) {
        foreach ($engine in 'fast', 'bold') {
            $path = Join-Path $outDir "$engine-$rows.pdf"
            $response = Invoke-WebRequest -UseBasicParsing "$BaseUrl/reports/$engine/$rows" -OutFile $path -PassThru
            $pages = [int]$response.Headers['X-Pdf-Pages']
            if ($response.Headers['Content-Type'] -ne 'application/pdf' -or
                $response.Headers['X-Pdf-Empty-Signatures'] -ne '3' -or
                ($rows -in 5, 17 -and $pages -ne 1) -or
                ($rows -in 18, 19, 23 -and $pages -ne 2) -or
                ($rows -ge 60 -and $pages -lt 2)) {
                throw "Invalid PDF result: $engine, $rows rows, $pages pages."
            }
            if ($run -gt 0) {
                $timings[$engine] += [pscustomobject]@{
                    EngineMs = [double]::Parse($response.Headers['X-Engine-Ms'], $culture)
                    TotalMs = [double]::Parse($response.Headers['X-Export-Ms'], $culture)
                    Pages = $pages
                    Bytes = (Get-Item $path).Length
                }
            }
        }
    }
    foreach ($engine in 'fast', 'bold') {
        [pscustomobject]@{
            Rows = $rows
            Engine = $engine
            Pages = $timings[$engine][0].Pages
            Bytes = $timings[$engine][0].Bytes
            MedianEngineMs = ($timings[$engine].EngineMs | Sort-Object)[2]
            MedianTotalMs = ($timings[$engine].TotalMs | Sort-Object)[2]
        }
    }
}
