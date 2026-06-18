param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $root "artifacts"
$packageName = "WinPet-$Version-$RuntimeIdentifier"
$publishDirectory = Join-Path $outputRoot $packageName
$archivePath = Join-Path $outputRoot "$packageName.zip"

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE"
    }
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
if ((Test-Path $publishDirectory) -and
    $publishDirectory.StartsWith($outputRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Remove-Item -LiteralPath $publishDirectory -Recurse
}

Invoke-DotNet restore (Join-Path $root "WinPet.sln") `
    --configfile (Join-Path $root "NuGet.Config")
Invoke-DotNet restore `
    (Join-Path $root "src/WinPet.Desktop/WinPet.Desktop.csproj") `
    --runtime $RuntimeIdentifier `
    --configfile (Join-Path $root "NuGet.Config")
Invoke-DotNet test (Join-Path $root "WinPet.sln") `
    --configuration Release `
    --no-restore
& (Join-Path $root "scripts/check-vulnerabilities.ps1") `
    -Solution (Join-Path $root "WinPet.sln")
if ($LASTEXITCODE -ne 0) {
    throw "Vulnerability scan failed."
}
Invoke-DotNet publish `
    (Join-Path $root "src/WinPet.Desktop/WinPet.Desktop.csproj") `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --no-restore `
    -p:Version=$Version `
    -p:PublishReadyToRun=false `
    --output $publishDirectory

if (Test-Path $archivePath) {
    Remove-Item -LiteralPath $archivePath
}

Compress-Archive -Path (Join-Path $publishDirectory "*") `
    -DestinationPath $archivePath

Write-Output $archivePath
