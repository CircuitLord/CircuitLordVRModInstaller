#Requires -Version 7
param(
    [Parameter(Mandatory = $true)][string]$PackageScript,
    [Parameter(Mandatory = $true)][string]$ManifestProperty,
    [switch]$SkipBuild
)

# stages an unpublished package, then opens the built installer against a local manifest holding it
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

$local = Join-Path $env:TEMP "circuitlord-local-test"
New-Item -ItemType Directory -Force -Path $local | Out-Null
$stage = Join-Path $env:TEMP "circuitlord-package-$PID"
$package = (Resolve-Path $PackageScript).Path
$options = @{ StageDir = $stage; SkipBuild = $SkipBuild }
# release symbols stay untouched by test builds
if ((Get-Command $package).Parameters.ContainsKey("NoSymbols")) { $options.NoSymbols = $true }
& $package @options
$metadataPath = Join-Path $stage ".package.json"
$metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
Remove-Item $metadataPath -Force

$packagePath = Join-Path $local "$($metadata.id)-$($metadata.version).zip"
Remove-Item $packagePath -Force -ErrorAction SilentlyContinue
Write-Host "compressing $packagePath..."
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $packagePath -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

$release = [pscustomobject]@{
    version = $metadata.version
    url = ([Uri]$packagePath).AbsoluteUri
    sha256 = (Get-FileHash $packagePath -Algorithm SHA256).Hash.ToLower()
    size = (Get-Item $packagePath).Length
}
$manifest = Read-Manifest
$entry = if ($ManifestProperty -eq "mods") { $manifest.mods | Where-Object { $_.id -eq $metadata.id } } else { $manifest.$ManifestProperty }
foreach ($name in @("version", "url", "sha256", "size")) { Set-Prop $entry $name $release.$name }
# both channels offer the local package
Set-Prop $entry beta $release
$manifestPath = Join-Path $local "manifest.json"
Write-Utf8 $manifestPath (($manifest | ConvertTo-Json -Depth 10) + "`n")

Write-Host "building CircuitLord's VR Mod Installer..."
dotnet build (Join-Path $PublicDir "src\Installer\CircuitLordVRModInstaller.csproj") -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }
$env:CIRCUITLORD_LOCAL_MANIFEST = $manifestPath
Start-Process (Join-Path $PublicDir "src\Installer\bin\Release\net48\CircuitLordVRModInstaller.exe")
Write-Host "opened the installer with local $($metadata.id) $($metadata.version) from $manifestPath"
