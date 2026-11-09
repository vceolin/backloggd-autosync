$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "C# compiler not found at $csc"
}

$binDir = Join-Path $PSScriptRoot "tests\bin"
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir | Out-Null
}

$testExe = Join-Path $binDir "TestRunner.exe"

# Gather all source files excluding MainForm.cs and Program.cs
$srcFiles = Get-ChildItem -Path (Join-Path $PSScriptRoot "src") -Recurse -Filter *.cs |
    Where-Object { $_.Name -ne "Program.cs" -and $_.Name -ne "MainForm.cs" } |
    Select-Object -ExpandProperty FullName

$testFiles = Get-ChildItem -Path (Join-Path $PSScriptRoot "tests") -Recurse -Filter *.cs |
    Select-Object -ExpandProperty FullName

$allSources = @($srcFiles) + @($testFiles)

Write-Host "Compiling test suite..." -ForegroundColor Cyan
& $csc /target:exe /out:$testExe /optimize+ `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    /r:System.Web.Extensions.dll `
    $allSources

if ($LASTEXITCODE -ne 0) {
    throw "Test compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Running tests..." -ForegroundColor Cyan
& $testExe
exit $LASTEXITCODE
