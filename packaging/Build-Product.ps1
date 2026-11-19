[CmdletBinding(DefaultParameterSetName='Unsigned')]
param(
    [string]$Compiler,
    [ValidatePattern('^\d+(\.\d+){2,3}$')][string]$Version,
    [string]$ReleaseRepository,
    [string]$UpdatePublicKey,
    [string]$Signer,
    [ValidateRange(1L,[long]::MaxValue)][long]$BuildSequence,
    [Parameter(Mandatory,ParameterSetName='Pem')][string]$PrivateKeyPem,
    [Parameter(Mandatory,ParameterSetName='Dpapi')][string]$ProtectedKey,
    [Parameter(Mandatory,ParameterSetName='Pem')]
    [Parameter(Mandatory,ParameterSetName='Dpapi')][string]$Notes,
    [Parameter(Mandatory,ParameterSetName='PayloadOnly')][switch]$PayloadOnly,
    [switch]$TestIdentity
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if(!$Version){$Version=([xml](Get-Content -LiteralPath (Join-Path $root 'MacDock.csproj') -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText}
if ([bool]$ReleaseRepository -ne [bool]$UpdatePublicKey) { throw 'Supply the repository and public verification key together.' }
if ($ReleaseRepository -and $ReleaseRepository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$') { throw 'Invalid GitHub repository.' }
if ($Notes -and !$ReleaseRepository) { throw 'Signing requires a channel-configured product build.' }
if ($UpdatePublicKey) {
    $keyBytes = [Convert]::FromBase64String($UpdatePublicKey)
    if ($PSVersionTable.PSEdition -eq 'Core') {
        $key = [Security.Cryptography.ECDsa]::Create()
        try {
            $used = 0
            $key.ImportSubjectPublicKeyInfo($keyBytes,[ref]$used)
            if ($key.KeySize -ne 256 -or $used -ne $keyBytes.Length -or $key.ExportParameters($false).Curve.Oid.Value -ne '1.2.840.10045.3.1.7') {
                throw 'The update key must be a P-256 SubjectPublicKeyInfo.'
            }
        } finally { $key.Dispose() }
    } elseif ($keyBytes.Length -ne 91) {
        throw 'The update key must be a P-256 SubjectPublicKeyInfo.'
    }
    $UpdatePublicKey = [Convert]::ToBase64String($keyBytes)
}
if (!$PayloadOnly) {
    if (!$Compiler) { $Compiler = (Get-Command ISCC.exe -ErrorAction Stop).Source }
    $Compiler = (Resolve-Path -LiteralPath $Compiler).Path
}
if ($Signer) { $Signer = (Resolve-Path -LiteralPath $Signer).Path }
function Invoke-Signer([string]$Path) {
    if (!$Signer) { return }
    & $Signer $Path
    if (!$? -or (Get-AuthenticodeSignature -LiteralPath $Path).Status -ne 'Valid') { throw "Authenticode verification failed for $Path" }
}

function Reserve-BuildSequence([long]$Requested) {
    $directory = Join-Path $root '.scratch\packaging'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $state = Join-Path $directory 'build-sequence.txt'
    $previous = 0L
    $stateExists = [IO.File]::Exists($state)
    if ($stateExists -and (![long]::TryParse([IO.File]::ReadAllText($state), [ref]$previous) -or $previous -le 0)) {
        throw "Invalid build sequence state: $state"
    }
    if ($previous -eq [long]::MaxValue) { throw 'The build sequence is exhausted.' }
    if ($Requested -gt 0 -and $Requested -le $previous) { throw "BuildSequence must be greater than $previous." }
    $next = if ($Requested -gt 0) { $Requested } else { [Math]::Max([DateTime]::UtcNow.Ticks, $previous + 1L) }
    $temporary = Join-Path $directory "$buildFingerprint.tmp"
    try {
        [IO.File]::WriteAllText($temporary, $next.ToString([Globalization.CultureInfo]::InvariantCulture))
        if ($stateExists) { [IO.File]::Replace($temporary, $state, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $state) }
    } finally {
        [IO.File]::Delete($temporary)
    }
    return $next
}

$mode = if ($TestIdentity) { 'test' } elseif ($PayloadOnly) { 'local' } else { 'candidate' }
$buildFingerprint = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $root ("release\product-" + $Version + '-' + $mode + '-' + $buildFingerprint)
$payload = Join-Path $staging 'MacDock'

# Use the same lock for sequence allocation and publish, which shares the workspace's obj and bin directories.
$sha = [Security.Cryptography.SHA256]::Create()
try { $workspaceHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($root.ToUpperInvariant()))).Replace('-','') }
finally { $sha.Dispose() }
$mutex = [Threading.Mutex]::new($false, "Global\MacDock.BuildProduct.$workspaceHash")
$ownsMutex = $false
try {
    try { $ownsMutex = $mutex.WaitOne() }
    catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    $BuildSequence = Reserve-BuildSequence $BuildSequence
    New-Item -ItemType Directory -Path $payload | Out-Null
    $sequenceText = $BuildSequence.ToString([Globalization.CultureInfo]::InvariantCulture)
    $publish = @('publish',(Join-Path $root 'MacDock.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--disable-build-servers',
        '-p:UseSharedCompilation=false','-p:PublishReadyToRun=true','-p:DebugType=none',"-p:Version=$Version",
        "-p:MacDockBuildSequence=$sequenceText","-p:MacDockBuildFingerprint=$buildFingerprint",'-o',$payload)
    if ($ReleaseRepository) { $publish += "-p:MacDockReleaseRepository=$ReleaseRepository", "-p:MacDockUpdatePublicKey=$UpdatePublicKey" }
    & dotnet @publish
    if ($LASTEXITCODE) { throw 'dotnet publish failed.' }
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

# Keep only distributable runtime files. Developer reference material is stored in the source tree.
foreach ($relative in @('Assets\Brand\SOURCE.md','Assets\Brand\README.txt','Assets\Brand\MacDock-256.png','Assets\Brand\MacDock-1024.png')) {
    $file = Join-Path $payload $relative
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
}
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $payload
Copy-Item -LiteralPath (Join-Path $root 'distribution\GUIDE.md') -Destination $payload
$deps = Get-Content -LiteralPath (Join-Path $payload 'MacDock.deps.json') -Raw | ConvertFrom-Json
$packages = & dotnet nuget locals global-packages --list
if ($LASTEXITCODE) { throw 'Unable to find the .NET runtime package license.' }
$cache = ($packages | Select-Object -First 1) -replace '^[^:]+:\s*',''
foreach ($name in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $entry = $deps.libraries.PSObject.Properties | Where-Object Name -like "runtimepack.$name/*" | Select-Object -First 1
    if (!$entry) { throw "Missing runtime library $name" }
    $location = Join-Path $cache ($entry.Name.Substring("runtimepack.".Length).ToLowerInvariant())
    $licenses = @(Get-ChildItem -LiteralPath $location -File | Where-Object Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$')
    if (!$licenses) { throw "Missing runtime license $name" }
    foreach ($notice in $licenses) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $payload ($name + '-' + $notice.Name))
    }
}
$unexpected = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Where-Object {
    $_.Name -match 'config\.json|last-session|\.pdb$|\.cs$|\.ps1$|\.zip$' -and $_.Name -ne 'MacDock.runtimeconfig.json'
})
if ($unexpected) { throw "Non-release file in payload: $($unexpected.Name -join ', ')" }
Invoke-Signer (Join-Path $payload 'MacDock.exe')
$payloadHashes = [ordered]@{}
foreach ($hname in @('MacDock.exe', 'MacDock.dll', 'MacDock.runtimeconfig.json')) {
    $hfile = Join-Path $payload $hname
    $payloadHashes[$hname] = (Get-FileHash -LiteralPath $hfile -Algorithm SHA256).Hash
}
[ordered]@{
    version = $Version; architecture = 'win-x64'; frameworkDependent = $false
    buildSequence = $BuildSequence; buildFingerprint = $buildFingerprint
    built = (Get-Date).ToUniversalTime().ToString('o'); sha256 = $payloadHashes
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'BUILDINFO.json') -Encoding UTF8
if ($PayloadOnly) {
    Write-Output $staging
    return
}
$arguments = @("/DBuildVersion=$Version","/DPayload=$payload","/DOutputFolder=$staging")
if ($TestIdentity) { $arguments += '/DProductId=MacDock.Desktop.Test','/DProductName=MacDock Release Test' }
& $Compiler @arguments (Join-Path $PSScriptRoot 'MacDock.iss')
if ($LASTEXITCODE) { throw 'Inno Setup compilation failed.' }
$setup = Join-Path $staging "MacDock-$Version-win-x64-Setup.exe"
Invoke-Signer $setup
$portable = Join-Path $staging "MacDock-$Version-win-x64-Portable.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$portable,[IO.Compression.CompressionLevel]::Optimal,$true)
$packageHashes = Get-FileHash -Algorithm SHA256 -LiteralPath $setup,$portable,(Join-Path $payload 'MacDock.dll'),(Join-Path $payload 'MacDock.exe')
$packageHashes | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)" } |
    Set-Content -LiteralPath (Join-Path $staging 'SHA256SUMS.txt') -Encoding ascii
[pscustomobject]@{
    folder = $staging; setup = $setup; portable = $portable; version = $Version
    testIdentity = [bool]$TestIdentity; channelConfigured = [bool]$ReleaseRepository
    releaseRepository = $ReleaseRepository; updatePublicKey = $UpdatePublicKey
    buildSequence = $BuildSequence; buildFingerprint = $buildFingerprint
    setupSha256 = $packageHashes[0].Hash.ToLowerInvariant(); payloadSha256 = $payloadHashes['MacDock.dll'].ToLowerInvariant()
} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging 'local-build.json')
if ($Notes) {
    $signing = @{ Setup = $setup; Notes = $Notes; AllowTestIdentity = $TestIdentity }
    if ($ProtectedKey) { $signing.ProtectedKey = $ProtectedKey }
    else { $signing.PrivateKeyPem = $PrivateKeyPem }
    & (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') @signing | Out-Null
}
Write-Output $staging
