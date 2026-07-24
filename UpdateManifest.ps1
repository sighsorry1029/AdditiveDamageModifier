[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string] $ManifestFile,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $VersionString
)

$resolvedManifestFile = (Resolve-Path -LiteralPath $ManifestFile -ErrorAction Stop).Path
$manifest = [System.IO.File]::ReadAllText($resolvedManifestFile)
$versionPattern = '"version_number":\s*"[^"]*"'

if (-not [System.Text.RegularExpressions.Regex]::IsMatch($manifest, $versionPattern)) {
    throw "version_number was not found in $resolvedManifestFile"
}

$updatedManifest = [System.Text.RegularExpressions.Regex]::Replace(
    $manifest,
    $versionPattern,
    "`"version_number`": `"$VersionString`"")

if ($updatedManifest -ne $manifest) {
    [System.IO.File]::WriteAllText(
        $resolvedManifestFile,
        $updatedManifest,
        [System.Text.UTF8Encoding]::new($false))
}
