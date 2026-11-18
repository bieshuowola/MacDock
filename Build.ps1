param([ValidateRange(1L,[long]::MaxValue)][long]$BuildSequence)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$releasePath = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
$release = Get-Item -LiteralPath $releasePath
$destination = Join-Path $release.FullName 'MacDock'
$program = Join-Path $destination 'MacDock.exe'
if ($release.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The release directory must be inside the workspace.' }

function Assert-ProgramStopped {
    if (Get-Process MacDock -ErrorAction SilentlyContinue | Where-Object Path -eq $program) {
        throw 'The deployed MacDock is running. Build to a separate staging directory; do not overwrite a live installation.'
    }
}

Assert-ProgramStopped
$lock = [IO.File]::Open((Join-Path $release.FullName '.local-build.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $options = @{
        PayloadOnly = $true
        ReleaseRepository = 'bieshuowola/MacDock'
        UpdatePublicKey = 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE8CV9b5ggMtBT9zqwguZ01C6KYD5IyzrKl4m9T00jQwFiPVtnZT+HKDwuAmNc9Mk29oIQgwj7OHQpqz+bj7kB9w=='
    }
    if ($PSBoundParameters.ContainsKey('BuildSequence')) { $options.BuildSequence = $BuildSequence }
    $stagingPath = & (Join-Path $PSScriptRoot 'packaging\Build-Product.ps1') @options | ForEach-Object {
        Write-Host $_
        $_
    } | Select-Object -Last 1
    $staging = Get-Item -LiteralPath $stagingPath
    $payload = Get-Item -LiteralPath (Join-Path $staging.FullName 'MacDock')
    if ($staging.Parent.FullName -ne $release.FullName -or !$payload.PSIsContainer -or
        (($staging.Attributes -bor $payload.Attributes) -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Expected the staged payload under the workspace release directory.'
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $payload.FullName '使用说明.md') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DESIGN.md') -Destination (Join-Path $payload.FullName '设计说明.md') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VALIDATION.md') -Destination (Join-Path $payload.FullName '验收记录.md') -Force
    $buildInfo = Join-Path $payload.FullName 'BUILDINFO.json'
    $info = Get-Content -LiteralPath $buildInfo -Raw | ConvertFrom-Json
    $info | Add-Member -NotePropertyName verification -NotePropertyValue 'Build only. GUI verification is recorded separately.'
    $info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $buildInfo -Encoding UTF8

    Assert-ProgramStopped
    $old = Get-Item -LiteralPath $destination -ErrorAction SilentlyContinue
    $rollback = Join-Path $staging.FullName 'previous-local'
    if ($old) {
        if (!$old.PSIsContainer -or ($old.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Expected a local release directory.' }
        [IO.Directory]::Move($old.FullName, $rollback)
    }
    try { [IO.Directory]::Move($payload.FullName, $destination) }
    catch {
        if ($old) { [IO.Directory]::Move($rollback, $destination) }
        throw
    }
    Write-Output $program
} finally { $lock.Dispose() }
