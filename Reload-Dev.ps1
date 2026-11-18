$ErrorActionPreference = 'Stop'

$RepoRoot = 'D:\Windows Dock\MacDock'
$ReleaseDir = Join-Path $RepoRoot 'release\MacDock'
$TargetExe = Join-Path $ReleaseDir 'MacDock.exe'
$TargetDll = Join-Path $ReleaseDir 'MacDock.dll'
$LogDir = Join-Path $RepoRoot '.scratch'
if (!(Test-Path -LiteralPath $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}
$LogFile = Join-Path $LogDir 'reload-dev.log'

function Write-Log([string]$Message) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] $Message"
    Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8
}

Set-Content -LiteralPath $LogFile -Value "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] Hot reload started." -Encoding UTF8

try {
    Stop-Process -Name 'MacDock' -Force -ErrorAction SilentlyContinue
    Write-Log 'Sent Stop-Process to MacDock.'

    $deadline = (Get-Date).AddSeconds(5)
    $released = $false
    while ((Get-Date) -lt $deadline) {
        $alive = Get-Process -Name 'MacDock' -ErrorAction SilentlyContinue
        if (-not $alive) {
            if (!(Test-Path -LiteralPath $TargetDll)) {
                $released = $true
                break
            }
            try {
                $stream = [System.IO.File]::Open($TargetDll, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
                $stream.Dispose()
                $released = $true
                break
            } catch {
                # File lock not yet released
            }
        }
        Start-Sleep -Milliseconds 200
    }

    if (-not $released) {
        throw "Timeout waiting for MacDock.exe exit or $TargetDll lock release."
    }
    Write-Log 'Confirmed MacDock exited and DLL lock released.'

    Set-Location -LiteralPath $RepoRoot
    & (Join-Path $RepoRoot 'Build.ps1') *>> $LogFile
    if ($LASTEXITCODE -ne 0) {
        throw "Build.ps1 failed with exit code $LASTEXITCODE"
    }
    Write-Log 'Build.ps1 completed successfully.'

    if (!(Test-Path -LiteralPath $TargetExe)) {
        throw "Missing built executable: $TargetExe"
    }

    Start-Process -FilePath $TargetExe -WorkingDirectory $ReleaseDir
    Write-Log "Started $TargetExe asynchronously."
} catch {
    Write-Log "ERROR: $($_.Exception.ToString())"
    throw
}
