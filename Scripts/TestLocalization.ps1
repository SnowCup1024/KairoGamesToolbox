[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$translations = Get-Content -LiteralPath (Join-Path $root 'Windows/Core/Localization.json') -Raw | ConvertFrom-Json -AsHashtable
$missing = [Collections.Generic.HashSet[string]]::new()
$count = 0
Get-ChildItem -LiteralPath (Join-Path $root 'Windows') -Recurse -File | Where-Object { $_.Extension -in @('.cs','.xaml') -and $_.FullName -notmatch '\\Test\\' } | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    foreach ($match in [regex]::Matches($text, 'L\.[TF]\("((?:[^"\\]|\\.)*)"|\{l:Text Key=''([^'']*)''\}')) {
        $key = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
        if ($key -notmatch '[\u4e00-\u9fff]') { continue }
        $count++
        if (-not $translations.ContainsKey($key)) { [void]$missing.Add($key); continue }
        foreach ($language in @('zh-TW','en','ja')) {
            if ([string]::IsNullOrWhiteSpace($translations[$key][$language])) { [void]$missing.Add("$language : $key") }
            $expected = @([regex]::Matches($key, '\{\d+(?:[^}]*)\}') | ForEach-Object Value | Sort-Object)
            $actual = @([regex]::Matches($translations[$key][$language], '\{\d+(?:[^}]*)\}') | ForEach-Object Value | Sort-Object)
            if (($expected -join '|') -ne ($actual -join '|')) { [void]$missing.Add("Placeholder mismatch $language : $key") }
        }
    }
}
if ($missing.Count) { throw ('Missing translations: ' + ($missing -join [Environment]::NewLine)) }
Write-Host "PASS: $count localized usages; three translation languages and format placeholders verified."
