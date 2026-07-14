[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReferenceRoot,

    [string]$Version = "dev",

    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ReferenceRoot = (Resolve-Path $ReferenceRoot).Path
$versionLabel = $Version.Trim()
if ($versionLabel.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
    $versionLabel = $versionLabel.Substring(1)
}
if ([string]::IsNullOrWhiteSpace($versionLabel)) {
    throw "Version must not be empty."
}

$requiredReferences = @(
    "kk\Assembly-CSharp.dll",
    "kk\KKAPI.dll",
    "kk\KKABMX.dll",
    "kks\KoikatsuSunshine_Data\Managed\Assembly-CSharp.dll",
    "kks\plugins\KKSAPI.dll",
    "kks\plugins\KKSABMX.dll"
)
foreach ($relativePath in $requiredReferences) {
    $fullPath = Join-Path $ReferenceRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Required reference is missing: $fullPath"
    }
}

if ($versionLabel -ne "dev" -and -not $versionLabel.StartsWith("ci-", [StringComparison]::OrdinalIgnoreCase)) {
    $pluginSource = Get-Content (Join-Path $repoRoot "src\HeelsSettings\HeelsPlugin.cs") -Raw
    $versionMatch = [regex]::Match($pluginSource, 'PluginVersion\s*=\s*"([^"]+)"')
    if (-not $versionMatch.Success -or $versionMatch.Groups[1].Value -ne $versionLabel) {
        throw "Release version '$versionLabel' does not match HeelsPlugin.PluginVersion."
    }
}

$projects = @(
    @{
        Name = "KK_HeelsSettings"
        Project = "src\HeelsSettings.Koikatu\KK_HeelsSettings.csproj"
        Framework = "net35"
    },
    @{
        Name = "KKS_HeelsSettings"
        Project = "src\HeelsSettings.KoikatsuSunshine\KKS_HeelsSettings.csproj"
        Framework = "net46"
    }
)

foreach ($entry in $projects) {
    $projectPath = Join-Path $repoRoot $entry.Project
    if (-not $NoRestore) {
        & dotnet restore $projectPath "-p:ReferenceRoot=$ReferenceRoot"
        if ($LASTEXITCODE -ne 0) { throw "Restore failed for $($entry.Name)." }
    }

    & dotnet build $projectPath --configuration Release --no-restore --no-incremental `
        "-p:ReferenceRoot=$ReferenceRoot" "-p:TreatWarningsAsErrors=true"
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $($entry.Name)." }

    if ($entry.Name -eq "KK_HeelsSettings") {
        $sourceDll = Join-Path $repoRoot "src\HeelsSettings.Koikatu\bin\Release\net35\KK_HeelsSettings.dll"
    }
    else {
        $sourceDll = Join-Path $repoRoot "src\HeelsSettings.KoikatsuSunshine\bin\Release\net46\KKS_HeelsSettings.dll"
    }

    $packageRoot = Join-Path $repoRoot "artifacts\staging\$($entry.Name)\BepInEx"
    $pluginDirectory = Join-Path $packageRoot "plugins"
    New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
    Copy-Item -LiteralPath $sourceDll -Destination $pluginDirectory -Force

    $archivePath = Join-Path $repoRoot "artifacts\$($entry.Name)-v$versionLabel.zip"
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -Force
    Write-Host "Created $archivePath"
}
