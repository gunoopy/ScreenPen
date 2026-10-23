# 설치 프로그램 빌드: ScreenPen.exe 를 최신으로 빌드한 뒤 ScreenPen_Setup.exe 안에 포함시킵니다.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# 1) 앱 빌드 (실행 중이면 종료)
Get-Process ScreenPen -ErrorAction SilentlyContinue | Stop-Process -Force
& "$PSScriptRoot\build.ps1"

# 2) 설치 프로그램 컴파일 (ScreenPen.exe 를 리소스로 포함)
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
& "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ `
    /out:ScreenPen_Setup.exe /win32icon:ScreenPen.ico /win32manifest:setup.manifest `
    /resource:ScreenPen.exe,ScreenPen.exe `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll Setup.cs
if ($LASTEXITCODE -ne 0) { throw "설치 프로그램 컴파일 실패" }
Write-Host "설치 프로그램 완료: $PSScriptRoot\ScreenPen_Setup.exe" -ForegroundColor Green
