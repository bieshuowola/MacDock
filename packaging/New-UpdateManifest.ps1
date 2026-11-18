param(
    [Parameter(Mandatory)][string]$Setup,
    [Parameter(Mandatory,ParameterSetName='Pem')][string]$PrivateKeyPem,
    [Parameter(Mandatory,ParameterSetName='Dpapi')][string]$ProtectedKey,
    [Parameter(Mandatory)][string]$Notes
)
$ErrorActionPreference='Stop'
$setupFile=Get-Item -LiteralPath $Setup
$nameMatch=[regex]::Match($setupFile.Name,'^MacDock-(\d+\.\d+\.\d+)-win-x64-Setup\.exe$')
if(!$nameMatch.Success){throw 'Unexpected setup filename.'}
$version=$nameMatch.Groups[1].Value
if($setupFile.VersionInfo.ProductVersion.Trim() -notmatch ('^'+[regex]::Escape($version)+'(?:\.0)?$')){throw 'Setup file version does not match the filename.'}
$workspace=Split-Path $PSScriptRoot
$keyPath=(Resolve-Path -LiteralPath $(if($ProtectedKey){$ProtectedKey}else{$PrivateKeyPem})).Path
if($keyPath.StartsWith($workspace.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Keep the private signing key outside the repository.'}
$manifest=[ordered]@{
    Version=$version; FileName=$setupFile.Name; Size=$setupFile.Length
    Sha256=(Get-FileHash -LiteralPath $setupFile.FullName).Hash.ToLowerInvariant()
    Notes=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $Notes).Path)
}
$key=[Security.Cryptography.ECDsa]::Create()
try {
    if($ProtectedKey){
        $secret=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyPath),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        try{$consumed=0;$key.ImportPkcs8PrivateKey($secret,[ref]$consumed);if($consumed -ne $secret.Length){throw 'Invalid protected key.'}}
        finally{[Array]::Clear($secret)}
    }else{$key.ImportFromPem([IO.File]::ReadAllText($keyPath))}
    if($key.KeySize -ne 256){throw 'Expected a P-256 update key.'}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress))
    if($bytes.Length -gt 65536){throw 'Update manifest exceeds size limit.'}
    $signature=$key.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    $jsonPath=Join-Path $setupFile.DirectoryName 'macdock-update.json'
    $sigPath=Join-Path $setupFile.DirectoryName 'macdock-update.sig'
    if((Test-Path $jsonPath) -or (Test-Path $sigPath)){throw 'Output exists. Use a fresh build directory.'}
    [IO.File]::WriteAllBytes($jsonPath,$bytes)
    [IO.File]::WriteAllBytes($sigPath,$signature)
    [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
} finally { $key.Dispose() }
