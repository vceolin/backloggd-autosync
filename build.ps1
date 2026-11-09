$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "C# compiler not found at $csc"
}

$outExe = Join-Path $PSScriptRoot "BackloggdAutoSync.exe"
$sources = (Get-ChildItem -Path (Join-Path $PSScriptRoot "src") -Recurse -Filter *.cs).FullName

$iconPath = Join-Path $PSScriptRoot "app.ico"
if (-not (Test-Path $iconPath)) {
    Write-Host "Generating app.ico..."
    Add-Type -AssemblyName System.Drawing
    $sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
    $pngStreams = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $rect = New-Object System.Drawing.RectangleF 0.5, 0.5, ($size - 1), ($size - 1)
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(234, 55, 122))
        $g.FillEllipse($brush, $rect)
        $brush.Dispose()

        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255)), ([Math]::Max(1.0, $size / 16.0))
        $inset = $pen.Width / 2.0
        $g.DrawEllipse($pen, $inset, $inset, $size - $pen.Width, $size - $pen.Width)
        $pen.Dispose()

        $fontSize = [float]($size * 0.55)
        $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
        $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
        $textRect = New-Object System.Drawing.RectangleF 0, 0, $size, ($size * 0.96)
        $g.DrawString('B', $font, $textBrush, $textRect, $sf)

        $font.Dispose()
        $textBrush.Dispose()
        $sf.Dispose()
        $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngStreams += ,@($size, $ms.ToArray())
        $ms.Dispose()
    }

    $fs = [System.IO.File]::Create($iconPath)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$pngStreams.Count)
    $offset = 6 + ($pngStreams.Count * 16)
    foreach ($item in $pngStreams) {
        $size = $item[0]
        $bytes = $item[1]
        $bSize = if ($size -ge 256) { [byte]0 } else { [byte]$size }
        $bw.Write($bSize)
        $bw.Write($bSize)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $bytes.Length
    }
    foreach ($item in $pngStreams) {
        $bw.Write($item[1])
    }
    $bw.Flush()
    $fs.Dispose()
}

Write-Host "Building BackloggdAutoSync.exe..."
Get-Process -Name "BackloggdAutoSync" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
& $csc /target:winexe /out:$outExe /optimize+ `
    "/win32icon:$iconPath" `
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
