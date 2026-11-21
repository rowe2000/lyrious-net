# Builds the Windows installer of the desktop app, on Windows with Inno Setup 6 installed
# (https://jrsoftware.org/isinfo.php; GitHub's Windows runners have it):
#
#   installers\windows\build-installer.ps1
#
# Out: installers\out\Lyrious-<version>-windows-x64-setup.exe, and a portable zip of the same app.
# Signing: unsigned unless WINDOWS_SIGN_PFX (a .pfx file) and WINDOWS_SIGN_PASSWORD are set.
# Unsigned, Windows SmartScreen warns about an unknown publisher ("More info" → "Run anyway").
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$out = Join-Path $root "installers\out"
$publish = Join-Path $out "publish-win-x64"
$project = Join-Path $root "Lyrious.Desktop\Lyrious.Desktop.csproj"

# The version: 0.1.2.7 for 7 commits after the tag 0.1.2 (as Directory.Build.props makes it).
$describe = git -C $root describe --tags --long --match "*[0-9]*" 2>$null
if (-not $describe) { $describe = "0.0.0-0-g0" }
$tag = $describe -replace '^v?(\d+(\.\d+){0,2}).*', '$1'
$count = $describe -replace '^.*-(\d+)-g[0-9a-f]+$', '$1'
while (($tag.Split('.')).Count -lt 3) { $tag = "$tag.0" }
$version = "$tag.$count"
Write-Host "Lyrious $version"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $publish -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if ($env:WINDOWS_SIGN_PFX) {
    & signtool sign /f $env:WINDOWS_SIGN_PFX /p $env:WINDOWS_SIGN_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 (Join-Path $publish "Lyrious.Desktop.exe")
}

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 isn't installed (ISCC.exe not found)." }
& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$out" (Join-Path $PSScriptRoot "Lyrious.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$setup = Join-Path $out "Lyrious-$version-windows-x64-setup.exe"
if ($env:WINDOWS_SIGN_PFX) {
    & signtool sign /f $env:WINDOWS_SIGN_PFX /p $env:WINDOWS_SIGN_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $setup
}

Compress-Archive -Path (Join-Path $publish "*") -DestinationPath (Join-Path $out "Lyrious-$version-windows-x64-portable.zip") -Force
Write-Host $setup
