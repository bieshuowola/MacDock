param(
 [Parameter(Mandatory)][string]$ArtifactFolder,
 [Parameter(Mandatory)][string]$Repository,
 [Parameter(Mandatory)][string]$Notes,
 [switch]$Authorized
)
$ErrorActionPreference='Stop'
if(!$Authorized){throw 'Obtain explicit authorization for the public distribution repository before creating its first draft.'}
if($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$'){throw 'Invalid repository.'}
$root=(Resolve-Path -LiteralPath $ArtifactFolder).Path
$build=Get-Content -LiteralPath (Join-Path $root 'local-build.json') -Raw|ConvertFrom-Json
if($build.testIdentity -or !$build.channelConfigured -or $build.releaseRepository -cne $Repository -or $build.version -notmatch '^\d+(\.\d+){2,3}$'){throw 'Build is not configured for this public distribution channel.'}
$repoJson=& gh repo view $Repository --json visibility,defaultBranchRef
if($LASTEXITCODE){throw 'Unable to inspect the public repository.'}
$repo=$repoJson|ConvertFrom-Json
if($repo.visibility -ne 'PUBLIC' -or !$repo.defaultBranchRef.name){throw 'Expected a reviewed public docs repository with a default branch.'}
$headJson=& gh api ("repos/$Repository/commits/"+[Uri]::EscapeDataString($repo.defaultBranchRef.name))
if($LASTEXITCODE){throw 'Unable to inspect the distribution commit.'}
$head=$headJson|ConvertFrom-Json
$treeJson=& gh api ("repos/$Repository/git/trees/"+$head.commit.tree.sha+"?recursive=1")
if($LASTEXITCODE){throw 'Unable to inspect the public tree.'}
$tree=$treeJson|ConvertFrom-Json
$allowed=@('README.md','GUIDE.md','CHANGELOG.md','dock.png','drawer.png','genie.png')
if($tree.truncated -or @($tree.tree).Count -ne $allowed.Count -or ($tree.tree|Where-Object {$_.type -ne 'blob' -or $_.mode -ne '100644' -or $_.path -cnotin $allowed})){
 throw 'Public tree is outside the docs-only allowlist.'
}
$sourceRoot=Split-Path $PSScriptRoot
foreach($file in $tree.tree){
 $reviewed=& git -C $sourceRoot rev-parse "HEAD:distribution/$($file.path)"
 if($LASTEXITCODE -or $reviewed -cne $file.sha){throw "Public document differs from reviewed content: $($file.path)"}
}
$version=$build.version
$tag="v$version"
$tagResponse=& gh api "repos/$Repository/git/matching-refs/tags/$tag"
if($LASTEXITCODE -or ($tagResponse|ConvertFrom-Json|Where-Object ref -ceq "refs/tags/$tag")){throw 'Release tag must be new so the draft targets the audited commit.'}
$setup="MacDock-$version-win-x64-Setup.exe"
$portable="MacDock-$version-win-x64-Portable.zip"
$files=@($setup,$portable,'SHA256SUMS.txt','macdock-update.json','macdock-update.sig')|ForEach-Object {Join-Path $root $_}
foreach($file in $files){if(!(Test-Path -LiteralPath $file -PathType Leaf)){throw "Required asset missing: $file"}}
$setupHash=(Get-FileHash -LiteralPath $files[0]).Hash
$portableHash=(Get-FileHash -LiteralPath $files[1]).Hash
$published=Get-Content -LiteralPath $files[2]
if($published.Count -ne 2 -or "$setupHash  $setup" -notin $published -or "$portableHash  $portable" -notin $published){throw 'SHA256SUMS differs from the assets.'}
$manifestBytes=[IO.File]::ReadAllBytes($files[3])
$signature=[IO.File]::ReadAllBytes($files[4])
$publicKey=[Convert]::FromBase64String($build.updatePublicKey)
$key=[Security.Cryptography.ECDsa]::Create()
try {
 $used=0
 $key.ImportSubjectPublicKeyInfo($publicKey,[ref]$used)
 if($used -ne $publicKey.Length -or $key.KeySize -ne 256 -or !$key.VerifyData($manifestBytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)){
  throw 'Manifest signature verification failed.'
 }
}finally{$key.Dispose()}
$manifest=[Text.Encoding]::UTF8.GetString($manifestBytes)|ConvertFrom-Json
if($manifest.Version -cne $version -or $manifest.FileName -cne $setup -or $manifest.Sha256 -ne $setupHash -or $manifest.Size -ne (Get-Item -LiteralPath $files[0]).Length){
 throw 'Signed manifest does not describe the actual installer.'
}
$notesPath=(Resolve-Path -LiteralPath $Notes).Path
& gh release create $tag @files --repo $Repository --target $head.sha --draft --title "MacDock $version" --notes-file $notesPath
if($LASTEXITCODE){throw 'Draft creation failed.'}
