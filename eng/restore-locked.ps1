[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Target
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$locks = @(Get-ChildItem $repositoryRoot -Filter packages.lock.json -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })

if ($locks.Count -eq 0) {
    Write-Warning 'No versioned packages.lock.json files exist yet; bootstrapping the initial lock graph.'
    & dotnet restore $Target --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw 'The initial NuGet restore failed.' }

    $locks = @(Get-ChildItem $repositoryRoot -Filter packages.lock.json -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    if ($locks.Count -eq 0) { throw 'Restore completed without producing package lock files.' }
}

& dotnet restore $Target --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'NuGet locked-mode restore failed.' }
