$ErrorActionPreference = "Stop"
$PublicDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$PublicSlug = "CircuitLord/CircuitLordVRModInstaller"
# older manifests are frozen for older installers
$ManifestPath = Join-Path $PublicDir "manifest-v3.json"

function Invoke-Native([string]$Exe, [string[]]$Arguments, [switch]$Quiet) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        if ($Quiet) { & $Exe @Arguments 2>&1 | Out-Null }
        else { & $Exe @Arguments 2>&1 | ForEach-Object { Write-Host $_ } }
        return $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
}

function Read-Native([string]$Exe, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { return (& $Exe @Arguments 2>$null) }
    finally { $ErrorActionPreference = $previous }
}

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding $false))
}

function Read-Manifest {
    $manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 3) { throw "manifest schema is not supported" }
    return $manifest
}

function Write-Manifest($Manifest) {
    Write-Utf8 $ManifestPath (($Manifest | ConvertTo-Json -Depth 10) + "`n")
}

function Set-Prop($Object, [string]$Name, $Value) {
    if ($Object.PSObject.Properties[$Name]) { $Object.$Name = $Value }
    else { $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
}

function Release-AssetUrl([string]$Tag, [string]$FileName) {
    return "https://github.com/$PublicSlug/releases/download/$Tag/$FileName"
}

function Publish-ReleaseAsset([string]$Path, [string]$Tag, [string]$Title, [string]$Notes, [switch]$Replace, [switch]$Prerelease) {
    if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw "gh CLI not found" }
    $exists = (Invoke-Native gh @("release", "view", $Tag, "--repo", $PublicSlug) -Quiet) -eq 0
    if ($exists) {
        $arguments = @("release", "upload", $Tag, $Path, "--repo", $PublicSlug)
        if ($Replace) { $arguments += "--clobber" }
        if ((Invoke-Native gh $arguments) -ne 0) { throw "release upload failed" }
        return
    }
    $arguments = @("release", "create", $Tag, $Path, "--repo", $PublicSlug, "--title", $Title, "--notes", $Notes, "--latest=false")
    if ($Prerelease) { $arguments += "--prerelease" }
    if ((Invoke-Native gh $arguments) -ne 0) {
        throw "release creation failed"
    }
}

function Commit-Manifest([string]$Message) {
    if ((Invoke-Native git @("-C", $PublicDir, "add", "--", (Split-Path $ManifestPath -Leaf))) -ne 0) { throw "git add failed" }
    if ((Invoke-Native git @("-C", $PublicDir, "commit", "-m", $Message)) -ne 0) { throw "git commit failed" }
    if ((Invoke-Native git @("-C", $PublicDir, "push")) -ne 0) { throw "git push failed" }
}
