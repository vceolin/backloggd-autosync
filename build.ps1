$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "C# compiler not found at $csc"
}

$outExe = Join-Path $PSScriptRoot "BackloggdAutoSync.exe"
$sources = @(
    (Join-Path $PSScriptRoot "src\Models.cs"),
    (Join-Path $PSScriptRoot "src\BackloggdClient.cs"),
    (Join-Path $PSScriptRoot "src\NintendoSwitchClient.cs"),
    (Join-Path $PSScriptRoot "src\SteamTracker.cs"),
    (Join-Path $PSScriptRoot "src\MainForm.cs"),
    (Join-Path $PSScriptRoot "src\Program.cs")
)

Write-Host "Building BackloggdAutoSync.exe..."
Get-Process -Name "BackloggdAutoSync" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
& $csc /target:winexe /out:$outExe /optimize+ `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    /r:System.Web.Extensions.dll `
    $sources

if ($LASTEXITCODE -eq 0) {
    Write-Host "Successfully built: $outExe" -ForegroundColor Green
} else {
    throw "Build failed with exit code $LASTEXITCODE"
}
