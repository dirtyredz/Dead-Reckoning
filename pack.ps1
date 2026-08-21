<#
    Builds a release archive laid out the way Nexus and Vortex expect:

        BepInEx/plugins/DeadReckoning/DeadReckoning.dll
        BepInEx/plugins/DeadReckoning/track-icon.png

    Deliberately not the dev deploy path (plugins/MoonlightPeaksMods/DeadReckoning), which only
    exists to keep hand-built DLLs clear of Vortex during development.

    Unlike the sibling mods, this one ships a second file: track-icon.png sits beside the DLL and
    is loaded at runtime for the Relationships "Track" button (see DRIcons / RelationshipTrackButton).
    The DeployPlugin target in the csproj copies it to the dev folder; this script puts it in the
    archive so players get it too. If it is ever missing the button silently falls back to text.

    No test project for this mod: every code path reads Unity and live game state - the soul-blob
    critter, Harmony patches, the map widgets, A* pathfinding - none of which a headless runner can
    assert. The checklist in RELEASING.md carries the weight here.
#>

$ErrorActionPreference = 'Stop'

$modRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $modRoot 'src\DeadReckoning.csproj'
$icon    = Join-Path $modRoot 'assets\track-icon.png'

# This mod lives in two places: on its own as its own repo, and inside the notes monorepo under
# mods/DeadReckoning. dist/ belongs at the repo root in both, so work out which layout is in play
# rather than assuming one.
$parentName = Split-Path -Leaf (Split-Path -Parent $modRoot)
if ($parentName -eq 'mods') {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $modRoot)
} else {
    $repoRoot = $modRoot
}

# Single source of truth for the version, so the archive can never disagree with the DLL.
$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ }
if (-not $version) { throw "Could not read <Version> from $project" }

Write-Host "Packing Dead Reckoning $version"

# SkipDeploy keeps a release build from overwriting the copy under test in the game folder.
dotnet build $project -c Release -p:SkipDeploy=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$dll = Join-Path $modRoot 'src\bin\Release\netstandard2.1\DeadReckoning.dll'
if (-not (Test-Path $dll))  { throw "Built DLL not found at $dll" }
if (-not (Test-Path $icon)) { throw "Track-button icon not found at $icon" }

$staging = Join-Path $env:TEMP "DeadReckoning-pack-$([guid]::NewGuid().ToString('N'))"
$target  = Join-Path $staging 'BepInEx\plugins\DeadReckoning'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $dll  $target
Copy-Item $icon $target

$dist = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$archive = Join-Path $dist "DeadReckoning-$version.zip"
if (Test-Path $archive) { Remove-Item $archive }

Compress-Archive -Path (Join-Path $staging 'BepInEx') -DestinationPath $archive
Remove-Item $staging -Recurse -Force

Write-Host "Created $archive"
Write-Host 'Extract it over the game folder to install.'
