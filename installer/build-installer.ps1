param([string]$Version = "1.0.3")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$dist = Join-Path $root 'dist'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    if (Test-Path $candidate) { $iscc = $candidate }
}
if (-not $iscc) { throw 'Inno Setup (ISCC.exe) was not found. Install Inno Setup 6 first.' }

Write-Host "== 1/4 Cleaning processes and old artifacts" -ForegroundColor Cyan
Get-Process SL -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "== 2/4 Publishing self-contained build" -ForegroundColor Cyan
& $dotnet publish (Join-Path $root 'src\SL.App\SL.App.csproj') -c Release -p:Platform=x64 `
    -r win-x64 --self-contained true -o $publish -v m
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host "== 3/4 Compiling installer" -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" (Join-Path $root 'installer\SL.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC compilation failed.' }

Write-Host "== 4/4 Creating portable package" -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $dist "SL-$Version-portable-win64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

Get-ChildItem $dist -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  " + $_.Name + "  " + [math]::Round($_.Length / 1MB, 1) + " MB")
}
Write-Host ("Done: " + $dist) -ForegroundColor Green