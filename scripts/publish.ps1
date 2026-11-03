[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\TinyX\TinyX.csproj'
$flavor = if ($SelfContained) { 'standalone' } else { 'framework-dependent' }
$packageName = "TinyX-$Runtime-$flavor"
$publishDirectory = Join-Path $projectRoot "dist\$packageName"
$archivePath = Join-Path $projectRoot "dist\$packageName.zip"

# A fresh staging folder prevents files from older publishes entering the archive.
$stagingDirectory = Join-Path $projectRoot ('work\publish-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
& $Dotnet publish $projectFile -c Release -r $Runtime --self-contained $SelfContained.IsPresent `
    -p:RestoreLockedMode=true -o $stagingDirectory -p:DebugType=None `
    -p:PublishReferencesDocumentationFiles=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $stagingDirectory -Recurse
$runningPackage = Get-Process -Name 'TinyX' -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -eq (Join-Path $publishDirectory 'TinyX.exe') } catch { $false }
}
if ($runningPackage) {
    $publishDirectory += '-update-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $stagingDirectory '*') -Destination $publishDirectory -Recurse -Force
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $archivePath -Force
Write-Output "Application: $(Join-Path $publishDirectory 'TinyX.exe')"
Write-Output "Archive: $archivePath"
