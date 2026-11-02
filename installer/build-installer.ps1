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

Write-Host "== 3/4 Compiling signed installer" -ForegroundColor Cyan
# Resolve signtool.exe (Windows SDK)
$signtool = $null
foreach ($base in @('C:\Program Files (x86)\Windows Kits\10\bin','C:\Program Files\Windows Kits\10\bin')) {
    if (Test-Path $base) {
        $found = Get-ChildItem $base -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { $signtool = $found.FullName; break }
    }
}
if (-not $signtool) { throw 'signtool.exe not found. Install the Windows SDK.' }

# Resolve or create the code-signing certificate (self-signed, test-only)
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -like '*SalmonLauncher*' } | Select-Object -First 1
if (-not $cert) {
    Write-Host "  Creating self-signed code-signing certificate (test-only)..." -ForegroundColor DarkGray
    $cert = New-SelfSignedCertificate -Type CodeSigningCert `
        -Subject 'CN=SalmonLauncher, O=Hugo Mou' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyUsage DigitalSignature -KeyUsageProperty Sign `
        -HashAlgorithm SHA256 -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears(10) `
        -FriendlyName 'SalmonLauncher Code Signing'
}
$thumbprint = $cert.Thumbprint
Write-Host "  signing with cert: $thumbprint" -ForegroundColor DarkGray

# Inno Setup SignTool definition: $f is replaced by the file to sign
# ISCC /S parameter contains quotes and spaces; use ProcessStartInfo to
# bypass PowerShell 5.1 argument parsing for native executables.
$issPath = Join-Path $root 'installer\SL.iss'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $iscc
$psi.Arguments = "/DAppVersion=$Version /S`"signtool=$signtool sign /sha1 $thumbprint /fd SHA256 `$f`" `"$issPath`""
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$proc = [System.Diagnostics.Process]::Start($psi)
$procOut = $proc.StandardOutput.ReadToEnd()
$proc.WaitForExit()
if ($proc.ExitCode -ne 0) { Write-Host $procOut; throw 'ISCC compilation failed.' }
if ($procOut -match 'Successfully signed') { Write-Host '  installer + uninstaller signed OK' -ForegroundColor DarkGray }

Write-Host "== 4/4 Creating portable package" -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $dist "SL-$Version-portable-win64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

Get-ChildItem $dist -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  " + $_.Name + "  " + [math]::Round($_.Length / 1MB, 1) + " MB")
}
Write-Host ("Done: " + $dist) -ForegroundColor Green