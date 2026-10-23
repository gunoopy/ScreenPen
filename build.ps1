# ScreenPen 빌드 스크립트 — .NET Framework 내장 컴파일러만 사용 (별도 설치 불필요)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$fw  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = "$fw\WPF"
if (-not (Test-Path "$fw\csc.exe")) { throw ".NET Framework 4 컴파일러를 찾을 수 없습니다." }

# 1) 아이콘 생성 (파란 둥근 사각형 + 흰색 펜)
Add-Type -AssemblyName System.Drawing
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 56; $d = $r * 2; $s = $size - 8
$path.AddArc(8, 8, $d, $d, 180, 90); $path.AddArc($s - $d, 8, $d, $d, 270, 90)
$path.AddArc($s - $d, $s - $d, $d, $d, 0, 90); $path.AddArc(8, $s - $d, $d, $d, 90, 90)
$path.CloseFigure()
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new($size, $size)), ([System.Drawing.Color]::FromArgb(10, 132, 255)), ([System.Drawing.Color]::FromArgb(94, 92, 230))
$g.FillPath($brush, $path)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 22
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
$g.DrawLine($pen, 82, 178, 178, 82)
$g.FillPolygon([System.Drawing.Brushes]::White, @([System.Drawing.Point]::new(62, 198), [System.Drawing.Point]::new(76, 160), [System.Drawing.Point]::new(100, 184)))
$wave = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 214, 10)), 12
$wave.StartCap = 'Round'; $wave.EndCap = 'Round'
$g.DrawBezier($wave, 120, 205, 150, 170, 175, 225, 205, 190)
$g.Dispose()
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$fs = [System.IO.File]::Create("$PSScriptRoot\ScreenPen.ico")
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)          # ICONDIR
$bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$png.Length); $bw.Write([uint32]22)
$bw.Write($png); $bw.Close(); $fs.Close(); $bmp.Dispose()

# 2) 컴파일
$refs = @(
  "$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll",
  "$fw\System.Xaml.dll", "$fw\System.Drawing.dll", "$fw\System.Windows.Forms.dll"
) | ForEach-Object { "/r:$_" }

& "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:ScreenPen.exe /win32icon:ScreenPen.ico /win32manifest:app.manifest @refs ScreenPen.cs
if ($LASTEXITCODE -ne 0) { throw "컴파일 실패" }
Write-Host "빌드 완료: $PSScriptRoot\ScreenPen.exe" -ForegroundColor Green
