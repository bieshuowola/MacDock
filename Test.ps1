param([switch]$Desktop)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:MACDOCK_TEST_RESULTS = Join-Path $PSScriptRoot 'test-results'
$program = Join-Path $PSScriptRoot 'release\MacDock\MacDock.exe'
foreach ($mode in @('--self-test') + $(if ($Desktop) { '--ui-test' })) {
    $process = Start-Process -FilePath $program -ArgumentList $mode -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(150000)) { Stop-Process -Id $process.Id; throw 'Test timed out. Inspect the desktop and test-results/progress.json.' }
    if ($process.ExitCode -ne 0) { throw "$mode exited with $($process.ExitCode)" }
    $file = if ($mode -eq '--self-test') { 'self-test.json' } else { 'ui-test.json' }
    $result = Get-Content -LiteralPath (Join-Path $env:MACDOCK_TEST_RESULTS $file) -Raw | ConvertFrom-Json
    if ($result | Where-Object { $_.error -or ($_.PSObject.Properties.Name -contains 'pass' -and -not $_.pass) }) { throw "$mode failed: inspect $file" }
    Write-Output "$mode passed: $($result.Count) checks"
}
