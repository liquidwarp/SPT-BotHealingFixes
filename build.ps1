param(
    [string]$SPTPath = 'E:\Games\SPT\4.1',

    [string]$HeadlessPath = 'E:\Games\SPT\4.1 Headless',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$InstallMod,

    [switch]$PackageRelease
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path "$SPTPath\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll")) {
    throw "SPT not found at '$SPTPath'"
}

if ($InstallMod -and $HeadlessPath -and -not (Test-Path "$HeadlessPath\BepInEx\plugins")) {
    throw "Headless not found at '$HeadlessPath'"
}

$buildArgs = @(
    "$PSScriptRoot\BotHealingFixes.slnx"
    "-c", $Configuration
    "-p:SPTPath=$SPTPath"
    "-p:HeadlessPath=$HeadlessPath"
    "-p:InstallMod=$($InstallMod.IsPresent)"
    "-p:PackageRelease=$($PackageRelease.IsPresent)"
    "-tlp:verbosity=detailed"
)
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
