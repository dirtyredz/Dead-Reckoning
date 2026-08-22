<#
    Builds a release archive laid out the way Nexus and Vortex expect:

        BepInEx/plugins/<ModName>/<ModName>.dll

    <ModName> is taken from the single .csproj under src/, so this script is generic: the same bytes
    work in every mod, whether it sits under mods/ in the Moonlight Peaks workspace or at the root of
    its own standalone repo. Deliberately not the dev deploy path
    (plugins/MoonlightPeaksMods/<ModName>), which only exists to keep hand-built DLLs clear of Vortex
    during development.

    GENERATED FILE - do not edit in a single mod. This is a verbatim copy of
    tools/pack.template.ps1 in the workspace. To change packing behaviour, edit the template and run
    tools/sync-pack.ps1 to re-distribute it to every mod.

    There is generally no test project to run: most code paths read Unity/game types a console runner
    cannot exercise. Per-mod verification lives in each mod's TESTING.md.
#>

$ErrorActionPreference = 'Stop'

$modRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

# The single project under src/ names the mod: src/<ModName>.csproj -> <ModName>. Require exactly
# one, so a stray second project can never be packaged nondeterministically.
$projects = @(Get-ChildItem -Path (Join-Path $modRoot 'src') -Filter '*.csproj' -File)
if ($projects.Count -eq 0) { throw "No .csproj found under $(Join-Path $modRoot 'src')" }
if ($projects.Count -gt 1) { throw "Expected exactly one .csproj under src/, found $($projects.Count): $($projects.Name -join ', ')" }
$project = $projects[0]
$modName = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)

# "ModNook" -> "Mod Nook", "PurrtasticPalette" -> "Purrtastic Palette" (cosmetic, for the log line).
$displayName = [regex]::Replace($modName, '([a-z])([A-Z])', '$1 $2')

# Single source of truth for the version, so the archive can never disagree with the DLL.
$version = @(([xml](Get-Content $project.FullName)).Project.PropertyGroup.Version | Where-Object { $_ })
if ($version.Count -ne 1) { throw "Expected exactly one non-empty <Version> in $($project.FullName), found $($version.Count)" }
$version = $version[0]

Write-Host "Packing $displayName $version"

# SkipDeploy keeps a release build from overwriting the copy under test in the game folder.
dotnet build $project.FullName -c Release -p:SkipDeploy=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$dll = Join-Path $modRoot "src\bin\Release\netstandard2.1\$modName.dll"
if (-not (Test-Path $dll)) { throw "Built DLL not found at $dll" }

$staging = Join-Path $env:TEMP "$modName-pack-$([guid]::NewGuid().ToString('N'))"
$target  = Join-Path $staging "BepInEx\plugins\$modName"
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $dll $target

# The mod always gets its own dist/, which is correct when the repo is cloned standalone.
$dist = Join-Path $modRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$archive = Join-Path $dist "$modName-$version.zip"
if (Test-Path $archive) { Remove-Item $archive }

Compress-Archive -Path (Join-Path $staging 'BepInEx') -DestinationPath $archive
Remove-Item $staging -Recurse -Force

Write-Host "Created $archive"
Write-Host 'Extract it over the game folder to install.'

# Convenience for the workspace only: sibling mods collect their archives in one shared dist/ two
# levels up. The guard is that the parent folder is literally named "mods" and a dist/ already exists
# beside it, neither of which is true for someone who clones this mod on its own.
$parent = Split-Path -Parent $modRoot
if ((Split-Path -Leaf $parent) -eq 'mods') {
    $sharedDist = Join-Path (Split-Path -Parent $parent) 'dist'

    if ((Test-Path $sharedDist -PathType Container) -and ((Resolve-Path $sharedDist).Path -ne (Resolve-Path $dist).Path)) {
        try {
            Copy-Item $archive $sharedDist -Force
            Write-Host "Also copied to $sharedDist"
        }
        catch {
            # A convenience copy failing must not fail the pack - the real archive already exists.
            Write-Warning "Could not copy to $sharedDist : $($_.Exception.Message)"
        }
    }
}
