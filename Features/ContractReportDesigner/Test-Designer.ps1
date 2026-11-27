param([string]$BaseUrl = 'http://localhost:5296', [string]$PdfPath)

$ErrorActionPreference = 'Stop'
$page = Invoke-WebRequest "$BaseUrl/contract-report-designer" -UseBasicParsing -SessionVariable designerSession
$token = [regex]::Match($page.Content, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
if (!$token) { throw 'Designer antiforgery token missing.' }
$url = "$BaseUrl/contract-report-designer/export"
$form = @{ __RequestVerificationToken = $token; output = 'frx'; columns = '[]'; objects = '[]' }
$blank = Invoke-WebRequest $url -Method Post -Body $form -WebSession $designerSession -UseBasicParsing
[xml]$empty = $blank.Content
if ($empty.Report.Dictionary -or $empty.Report.ReportPage.ChildNodes.Count -ne 0) {
    throw 'A blank design should contain only a page.'
}

$form.objects = @(
    @{ Id = 1; Kind = 'text'; X = 30; Y = 20; Width = 220; Height = 40; Text = 'Standalone report'; Align = 'Left';
        Typography = @{ Font = 'Arial'; Size = 16; Bold = $true; Italic = $true; Underline = $true; Color = '#1256A0' } }
    @{ Id = 2; Kind = 'signature'; X = 35; Y = 105; Width = 170; Height = 70; Text = ''; FontSize = 12; Bold = $false; Align = 'Left' }
    @{ Id = 3; Kind = 'ellipse'; X = 280; Y = 63; Width = 95; Height = 60; Text = ''; FontSize = 12; Bold = $false; Align = 'Left' }
    @{ Id = 4; Kind = 'line'; X = 15; Y = 180; Width = 140; Height = 8; Text = ''; FontSize = 12; Bold = $false; Align = 'Left' }
    @{ Id = 5; Kind = 'image'; X = 430; Y = 35; Width = 90; Height = 70; Text = ''; FontSize = 12; Bold = $false; Align = 'Left';
        Image = 'iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAWSURBVChTY3DMfPgfH2ZAF0DHw0MBAHmlooHA5OB1AAAAAElFTkSuQmCC' }
    @{ Id = 6; Kind = 'rectangle'; X = 410; Y = 15; Width = 130; Height = 110; Text = ''; FontSize = 12; Bold = $false; Align = 'Left' }
    @{ Id = 7; Kind = 'signature'; X = 350; Y = 300; Width = 170; Height = 70; Text = ''; Align = 'Left' }
    @{ Id = 8; Kind = 'signature'; X = 650; Y = 300; Width = 170; Height = 70; Text = ''; Align = 'Left' }
) | ConvertTo-Json -Compress -Depth 4
$response = Invoke-WebRequest $url -Method Post -Body $form -WebSession $designerSession -UseBasicParsing
[xml]$template = $response.Content
$canvas = $template.Report.ReportPage.ReportTitleBand
if ($template.Report.Dictionary -or $canvas.TextObject.Text -ne 'Standalone report' -or
    $canvas.TextObject.Font -ne 'Arial, 16pt, style=Bold, Italic, Underline' -or
    $canvas.TextObject.GetAttribute('TextFill.Color') -ne '18, 86, 160' -or
    ($canvas.DigitalSignatureObject.Left -join ',') -ne '35,350,650' -or $canvas.ShapeObject[0].Shape -ne 'Ellipse' -or
    $canvas.LineObject.Width -ne '140' -or !$canvas.PictureObject.Image -or $canvas.ShapeObject.Count -ne 2) {
    throw 'Free-form FRX objects or their positions are incorrect.'
}

if ($PdfPath) {
    $form.output = 'pdf'
    Invoke-WebRequest $url -Method Post -Body $form -WebSession $designerSession -UseBasicParsing -OutFile $PdfPath
    $form.output = 'frx'
}

$form.columns = '[{"Id":2,"Label":"Description","Width":95,"Align":"Left","HeaderSpan":2},{"Id":1,"Label":"Number","Width":80,"Align":"Center"}]'
$form.tableX = '123'
$form.tableY = '195'
$form.dataRows = '2'
$form.merges = '[{"ColumnId":2,"Row":1,"Span":2}]'
$form.tableTypography = '{"Font":"Verdana","Size":14,"Italic":true,"Color":"#5B2090"}'
$response = Invoke-WebRequest $url -Method Post -Body $form -WebSession $designerSession -UseBasicParsing
[xml]$template = $response.Content
$table = $template.Report.ReportPage.ReportTitleBand.TableObject
if ($table.Left -ne '123' -or $table.Top -ne '195' -or $table.Width -ne '175' -or
    $table.TableColumn[0].Width -ne '95' -or $table.TableRow[0].TableCell[0].Text -ne 'Description' -or
    $table.TableRow[0].TableCell[0].ColSpan -ne '2' -or $table.TableRow[1].TableCell.Count -ne 2 -or
    $table.TableRow.Count -ne 3 -or $table.TableRow[1].TableCell[0].RowSpan -ne '2' -or
    $table.TableRow[0].TableCell[0].Font -ne 'Verdana, 14pt, style=Italic' -or
    $table.TableRow[1].TableCell[0].GetAttribute('TextFill.Color') -ne '91, 32, 144' -or
    $template.Report.ReportPage.ReportTitleBand.DigitalSignatureObject.Count -ne 3 -or
    $template.Report.ReportPage.ReportSummaryBand) {
    throw 'FRX table placement, columns, or signature objects are incorrect.'
}
if ($PdfPath) {
    $form.output = 'pdf'
    Invoke-WebRequest $url -Method Post -Body $form -WebSession $designerSession -UseBasicParsing -OutFile $PdfPath
    $form.output = 'frx'
}

$form.merges = '[{"ColumnId":2,"Row":0,"Span":2}]'
try {
    Invoke-WebRequest $url -Method Post -Body $form -WebSession $designerSession -UseBasicParsing
    throw 'The endpoint accepted overlapping horizontal and vertical merges.'
}
catch {
    if ([int]$_.Exception.Response.StatusCode -ne 400) { throw }
}

$form.columns = '[{"Id":1,"Label":"TABLE ALPHA BRAVO CHARLIE DELTA","Width":90,"Align":"Left"}]'
$form.objects = '[{"Id":1,"Kind":"text","X":30,"Y":20,"Width":100,"Height":24,"Text":"TEXT ECHO FOXTROT GOLF HOTEL","Align":"Left","Typography":{"AutoSize":true,"WordWrap":true}}]'
$form.tableTypography = '{"Size":12,"AutoSize":true,"WordWrap":true}'
$form.merges = '[]'
$response = Invoke-WebRequest $url -Method Post -Body $form -WebSession $designerSession -UseBasicParsing
[xml]$template = $response.Content
$canvas = $template.Report.ReportPage.ReportTitleBand
$header = $canvas.TableObject.TableRow[0]
if ($canvas.TextObject.CanGrow -ne 'true' -or $canvas.TextObject.WordWrap -ne 'true' -or
    $header.AutoSize -ne 'true' -or $header.MinHeight -ne '30' -or $header.TableCell.WordWrap -ne 'true') {
    throw 'FRX text growth or table wrapping attributes are incorrect.'
}
if ($PdfPath) {
    $form.output = 'pdf'
    $autoSizePdf = [IO.Path]::ChangeExtension($PdfPath, '.autosize.pdf')
    [IO.File]::WriteAllText([IO.Path]::ChangeExtension($PdfPath, '.autosize.frx'), $response.Content)
    Invoke-WebRequest $url -Method Post -Body $form -WebSession $designerSession -UseBasicParsing -OutFile $autoSizePdf
}

Write-Output 'Blank, free-form, and table FRX export checks passed.'
