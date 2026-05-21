#requires -Version 5.1
<#
.SYNOPSIS
    Mass-convert Flowseal/zapret-discord-youtube .bat strategies into ZDefree JSON.

.DESCRIPTION
    Fetches every *.bat (except service.bat) from the Flowseal repo via the
    GitHub Contents API, downloads each into a temp dir, then runs
    `zdefree bat2json` to convert it into a ZDefree-flavored JSON strategy.

    Output goes to `<ZDefreeRoot>\strategies\common\<id>.json`.

    Unknown-flag warnings from the CLI are aggregated into a log file under
    `scripts\flowseal-warnings.log` so we can decide which raw_args flags
    deserve a typed schema entry.

.PARAMETER ZDefreeRoot
    Path to the ZDefree distribution root (the folder containing manifest.json).
    Defaults to ..\..\ZDefree relative to this script (works when the two repos
    are side-by-side under GUI\).

.PARAMETER ToolsRoot
    Path to the ZDefree-tools root (the folder containing ZDefree.Tools.sln).
    Defaults to .. relative to this script.

.PARAMETER Force
    Overwrite existing strategy files. Without -Force, existing files are
    skipped and reported as such.

.EXAMPLE
    pwsh .\scripts\Convert-FlowsealBatch.ps1 -Force
#>
[CmdletBinding()]
param(
    [string]$ZDefreeRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\ZDefree')).Path,
    [string]$ToolsRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ApiRoot      = 'https://api.github.com/repos/Flowseal/zapret-discord-youtube/contents/'
$UserAgent    = 'ZDefree-tools'
$CliProject   = Join-Path $ToolsRoot 'src\ZDefree.Cli\ZDefree.Cli.csproj'
$CommonDir    = Join-Path $ZDefreeRoot 'strategies\common'
$WarnLog      = Join-Path $PSScriptRoot 'flowseal-warnings.log'
$TempDir      = Join-Path ([System.IO.Path]::GetTempPath()) "flowseal-bats-$([Guid]::NewGuid().ToString('N'))"

if (-not (Test-Path $CommonDir)) {
    throw "strategies\common not found: $CommonDir"
}
if (-not (Test-Path $CliProject)) {
    throw "ZDefree.Cli project not found: $CliProject"
}

# Ensure CLI built once up front (Release for speed).
Write-Host '[1/4] Building zdefree CLI (Release)...' -ForegroundColor Cyan
& dotnet build $CliProject -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }
$CliDll = Join-Path $ToolsRoot 'src\ZDefree.Cli\bin\Release\net8.0\zdefree.dll'
if (-not (Test-Path $CliDll)) { throw "Built CLI dll not found: $CliDll" }

Write-Host '[2/4] Listing Flowseal .bat strategies...' -ForegroundColor Cyan
$listing = Invoke-RestMethod -Uri $ApiRoot -Headers @{ 'User-Agent' = $UserAgent }
$bats = @($listing | Where-Object { $_.name -like '*.bat' -and $_.name -ne 'service.bat' })
Write-Host ("       {0} .bat files found" -f $bats.Count)

New-Item -ItemType Directory -Path $TempDir | Out-Null
if (Test-Path $WarnLog) { Remove-Item $WarnLog }

$converted = 0
$skipped   = 0
$failed    = 0
$allWarnings = [System.Collections.Generic.List[string]]::new()

Write-Host '[3/4] Downloading + converting...' -ForegroundColor Cyan
foreach ($bat in $bats) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($bat.name)

    # Mirror BatchToStrategy.DeriveId: lowercase, non-alnum → '-', trim '-', collapse '--' → '-'.
    $id = ($stem.ToLowerInvariant().ToCharArray() |
        ForEach-Object { if ([char]::IsLetterOrDigit($_)) { $_ } else { '-' } }) -join ''
    $id = $id.Trim('-').Replace('--', '-')

    $tempBat = Join-Path $TempDir $bat.name
    $outJson = Join-Path $CommonDir ("{0}.json" -f $id)

    if ((Test-Path $outJson) -and -not $Force) {
        Write-Host ("  - {0,-40} -> skip (exists)" -f $bat.name) -ForegroundColor DarkGray
        $skipped++
        continue
    }

    try {
        Invoke-WebRequest -Uri $bat.download_url -Headers @{ 'User-Agent' = $UserAgent } `
            -OutFile $tempBat -UseBasicParsing | Out-Null

        # PS 5.1 Start-Process -ArgumentList @() doesn't quote elements with spaces,
        # and `& dotnet ... 2>&1` wraps stderr as ErrorRecord under $EAP='Stop'.
        # Use ProcessStartInfo with manually quoted args + direct stream capture.
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = 'dotnet'
        $psi.Arguments = ('"{0}" bat2json "{1}" "{2}" --name "{3}"' -f $CliDll, $tempBat, $outJson, $stem)
        $psi.UseShellExecute        = $false
        $psi.RedirectStandardError  = $true
        $psi.RedirectStandardOutput = $true
        $psi.CreateNoWindow         = $true

        $p = [System.Diagnostics.Process]::Start($psi)
        $stderrText = $p.StandardError.ReadToEnd()
        $null       = $p.StandardOutput.ReadToEnd()
        $p.WaitForExit()
        $exitCode    = $p.ExitCode
        $stderrLines = $stderrText -split "`r?`n" | Where-Object { $_ }

        if ($exitCode -ne 0) {
            $failed++
            Write-Host ("  ! {0,-40} FAILED (exit {1})" -f $bat.name, $exitCode) -ForegroundColor Red
            $stderrLines | ForEach-Object { Write-Host "      $_" -ForegroundColor Red }
            continue
        }

        # Post-process: enrich with category derived from filename + today's tested date.
        # bat2json can't infer these from the .bat itself, but they're useful catalog metadata.
        $category = switch -Regex ($stem.ToUpperInvariant()) {
            'FAKE\s+TLS'   { 'fake-tls';    break }
            'SIMPLE\s+FAKE'{ 'simple-fake'; break }
            'ALT'          { 'alt';         break }
            default        { 'general' }
        }
        $today = (Get-Date).ToString('yyyy-MM-dd')
        try {
            $json = Get-Content $outJson -Raw | ConvertFrom-Json
            # WhenWritingNull suppresses unset fields, so use Add-Member -Force to set-or-create.
            $json | Add-Member -NotePropertyName category -NotePropertyValue $category -Force
            $json | Add-Member -NotePropertyName tested   -NotePropertyValue $today    -Force
            $serialized = $json | ConvertTo-Json -Depth 64
            Set-Content -Path $outJson -Value $serialized -Encoding utf8
        } catch {
            $allWarnings.Add("[$($bat.name)] post-process failed: $($_.Exception.Message)")
        }

        $warnings = @($stderrLines | Where-Object { $_ -match 'Unknown flag' })
        if ($warnings.Count -gt 0) {
            foreach ($w in $warnings) {
                $allWarnings.Add("[$($bat.name)] $($w.Trim())")
            }
            Write-Host ("  ~ {0,-40} ok ({1} warnings)" -f $bat.name, $warnings.Count) -ForegroundColor Yellow
        } else {
            Write-Host ("  + {0,-40} ok" -f $bat.name) -ForegroundColor Green
        }
        $converted++
    }
    catch {
        $failed++
        Write-Host ("  ! {0,-40} EXCEPTION" -f $bat.name) -ForegroundColor Red
        Write-Host "      $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host '[4/4] Summary' -ForegroundColor Cyan
Write-Host ("       converted: {0}" -f $converted)
Write-Host ("       skipped  : {0}" -f $skipped)
Write-Host ("       failed   : {0}" -f $failed)
Write-Host ("       warnings : {0}" -f $allWarnings.Count)

if ($allWarnings.Count -gt 0) {
    $allWarnings | Out-File -FilePath $WarnLog -Encoding utf8
    Write-Host ("       log      : {0}" -f $WarnLog) -ForegroundColor Yellow

    # Aggregate by flag name to highlight high-recurrence ones.
    $byFlag = @{}
    foreach ($w in $allWarnings) {
        if ($w -match 'Unknown flag passed through to advanced\.raw_args:\s*(\S+)') {
            $flag = $matches[1]
            if (-not $byFlag.ContainsKey($flag)) { $byFlag[$flag] = 0 }
            $byFlag[$flag]++
        }
    }
    Write-Host ''
    Write-Host '       unknown flag occurrences (>=1):' -ForegroundColor Yellow
    $byFlag.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
        Write-Host ("         {0,3}x  {1}" -f $_.Value, $_.Key)
    }
}

Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue

if ($failed -gt 0) { exit 1 } else { exit 0 }
