param(
    [Parameter(Mandatory = $true)][string]$PackageScript,
    [Parameter(Mandatory = $true)][string]$ManifestProperty,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [switch]$Beta,
    [switch]$SkipBuild,
    [switch]$NoPush
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

if (!$NoPush -and (Read-Native git @("-C", $PublicDir, "status", "--porcelain"))) {
    throw "commit the installer repo before publishing"
}

$packageScriptPath = (Resolve-Path $PackageScript).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$stage = Join-Path $env:TEMP "circuitlord-package-$PID"
& $packageScriptPath -StageDir $stage -SkipBuild:$SkipBuild

$metadataPath = Join-Path $stage ".package.json"
if (!(Test-Path $metadataPath)) { throw "package metadata is missing" }
$metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
Remove-Item $metadataPath -Force

$fileName = if ($Beta) { "$($metadata.id)-beta-$($metadata.version).zip" } else { "$($metadata.id)-$($metadata.version).zip" }
$packagePath = Join-Path $OutputDir $fileName
Remove-Item $packagePath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $packagePath -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

$releaseTag = "mod-packages"
$release = [pscustomobject]@{
    version = $metadata.version
    url = Release-AssetUrl $releaseTag $fileName
    sha256 = (Get-FileHash $packagePath -Algorithm SHA256).Hash.ToLower()
    size = (Get-Item $packagePath).Length
}

$manifest = Read-Manifest
if ($ManifestProperty -eq "mods") {
    $entry = $manifest.mods | Where-Object { $_.id -eq $metadata.id }
    if (!$entry) {
        $entry = [pscustomobject]@{ id = $metadata.id }
        $manifest.mods = @($manifest.mods) + $entry
    }
}
else {
    $entry = $manifest.$ManifestProperty
    if (!$entry) {
        $entry = [pscustomobject]@{}
        Set-Prop $manifest $ManifestProperty $entry
    }
}

Set-Prop $entry id $metadata.id
Set-Prop $entry name $metadata.name
Set-Prop $entry author $metadata.author
Set-Prop $entry description $metadata.description
foreach ($name in @("core", "preserve", "tokenize")) {
    if ($metadata.PSObject.Properties[$name]) { Set-Prop $entry $name $metadata.$name }
}
if ($Beta) {
    Set-Prop $entry beta $release
}
else {
    Set-Prop $entry version $release.version
    Set-Prop $entry url $release.url
    Set-Prop $entry sha256 $release.sha256
    Set-Prop $entry size $release.size
    if (!$entry.beta -or (Is-VersionNotNewer $entry.beta.version $release.version)) { Set-Prop $entry beta $release }
}
Write-Manifest $manifest

$channel = if ($Beta) { "beta" } else { "stable" }
if ($NoPush) {
    Write-Host "prepared $channel $($metadata.id) $($metadata.version)"
    Write-Host "package: $packagePath"
    if ($metadata.symbols) { Write-Host "symbols: $($metadata.symbols)" }
    return
}

Publish-ReleaseAsset -Path $packagePath -Tag $releaseTag -Title "Mod packages" -Notes "Versioned packages used by CircuitLord's VR Mod Installer."
Commit-Manifest "update mod package $($metadata.version)"
Write-Host "published $channel $($metadata.id) $($metadata.version)"
