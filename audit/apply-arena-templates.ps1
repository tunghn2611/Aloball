$ErrorActionPreference = 'Stop'
$rootPath = 'D:\AloBall\AloBall.Web'
$utf8 = New-Object System.Text.UTF8Encoding($false)
Get-ChildItem "$rootPath\Views" -Recurse -Filter '*.cshtml' | ForEach-Object {
    $source = [IO.File]::ReadAllText($_.FullName)
    $source = [regex]::Replace($source, '(?<![a-zA-Z])ab-', 'arena-')
    $source = $source.Replace('class="d-flex justify-content-between align-items-center mb-3 flex-wrap gap-2"', 'class="page-heading"')
    $source = $source.Replace('class="d-flex justify-content-between align-items-center mb-4 flex-wrap gap-2"', 'class="page-heading"')
    # Move page titles into the new heading hierarchy without touching modal titles.
    if ($_.Directory.Name -notin @('Shared','Home') -and $_.Name -notlike '_*') {
        $source = [regex]::Replace($source, '<h[34]( class="[^"]*")?>([^<]*(?:<i[^>]*></i>)?[^<]+)</h[34]>', {
            param($m)
            if ($m.Index -lt 2800) { return '<h1' + $m.Groups[1].Value + '>' + $m.Groups[2].Value + '</h1>' }
            return $m.Value
        })
    }
    # Remove former branding comments and change decorative theme colors.
    $source = [regex]::Replace($source, '<!--[^>]*(?:PHONG CÁCH CAHN FC|SPORTS HERO BANNER|Mobile-First Bottom)[^>]*-->', '')
    $source = $source.Replace('#10b981','#f15b3b').Replace('#059669','#c53f25').Replace('#064e3b','#182033').Replace('#d1fae5','#fff0ea').Replace('#ecfdf5','#fff6f1')
    [IO.File]::WriteAllText($_.FullName, $source, $utf8)
}
$worker = [IO.File]::ReadAllText("$rootPath\wwwroot\sw.js")
$worker = $worker.Replace('aloball-cache-v2','aloball-arena-v3').Replace('/css/site.css','/css/arena.css').Replace('/js/site.js','/js/arena.js')
[IO.File]::WriteAllText("$rootPath\wwwroot\sw.js", $worker, $utf8)
