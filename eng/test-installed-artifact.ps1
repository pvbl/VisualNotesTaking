[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Installer,
    [string] $PreviousInstaller
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:ProgramFiles 'VisualNotes'
$executable = Join-Path $installDirectory 'VisualNotes.App.exe'
$uninstaller = Join-Path $installDirectory 'unins000.exe'
$dataDirectory = Join-Path $env:LOCALAPPDATA 'VisualNotes'
$sentinel = Join-Path $dataDirectory 'installer-preservation.sentinel'
$marker = Join-Path $env:TEMP "visualnotes-installed-smoke-$PID.txt"

function Install([string] $Path) {
    $process = Start-Process $Path -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer exited with $($process.ExitCode): $Path" }
    if (-not (Test-Path $executable)) { throw "Installed executable was not found: $executable" }
}

try {
    # Clean install (or install a baseline when validating an upgrade).
    Install $(if ($PreviousInstaller) { $PreviousInstaller } else { $Installer })
    New-Item $dataDirectory -ItemType Directory -Force | Out-Null
    Set-Content $sentinel 'must survive update, repair and uninstall'

    if ($PreviousInstaller) { Install $Installer } # update
    Install $Installer # repair/reinstall
    if (-not (Test-Path $sentinel)) { throw 'User data was removed during update or repair.' }

    $process = Start-Process $executable -ArgumentList '--smoke-test', "`"$marker`"" -Wait -PassThru
    if ($process.ExitCode -ne 0 -or -not (Test-Path $marker)) { throw 'The installed application smoke test failed.' }

    $process = Start-Process $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Uninstaller exited with $($process.ExitCode)." }
    if (Test-Path $executable) { throw 'Application files remain after uninstall.' }
    if (-not (Test-Path $sentinel)) { throw 'Uninstall removed user sessions or configuration.' }
}
finally {
    Remove-Item $marker -Force -ErrorAction SilentlyContinue
    Remove-Item $sentinel -Force -ErrorAction SilentlyContinue
}
