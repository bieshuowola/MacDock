$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (Get-Process MacDock -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $PSScriptRoot 'release\MacDock\MacDock.exe')) {
    throw 'The deployed MacDock is running. Build to a separate staging directory; do not overwrite a live installation.'
}
dotnet publish -c Release -r win-x64 --self-contained true -p:UseSharedCompilation=false --disable-build-servers -p:PublishReadyToRun=true -p:DebugType=none -p:MacDockReleaseRepository=bieshuowola/MacDock -p:MacDockUpdatePublicKey=MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE8CV9b5ggMtBT9zqwguZ01C6KYD5IyzrKl4m9T00jQwFiPVtnZT+HKDwuAmNc9Mk29oIQgwj7OHQpqz+bj7kB9w== -o release\MacDock
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'release\MacDock\使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DESIGN.md') -Destination (Join-Path $PSScriptRoot 'release\MacDock\设计说明.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VALIDATION.md') -Destination (Join-Path $PSScriptRoot 'release\MacDock\验收记录.md') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $PSScriptRoot 'release\MacDock\THIRD-PARTY-NOTICES.md') -Force
$hashes = [ordered]@{}
foreach ($name in @('MacDock.exe', 'MacDock.dll', 'MacDock.runtimeconfig.json')) {
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('release\MacDock\' + $name)) -Algorithm SHA256).Hash
}
$buildInfo = [ordered]@{
    version = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'MacDock.csproj') -Raw)).Project.PropertyGroup.Version; architecture = 'win-x64'; frameworkDependent = $false
    built = (Get-Date).ToString('o'); sha256 = $hashes
    verification = 'Build only; GUI verification is a separate step. See the dated validation records.'
}
$buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'release\MacDock\BUILDINFO.json') -Encoding UTF8
Write-Output (Join-Path $PSScriptRoot 'release\MacDock\MacDock.exe')
