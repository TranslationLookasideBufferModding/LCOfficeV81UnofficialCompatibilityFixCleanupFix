$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$manifestPath=Join-Path $repo 'package\manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$dll=Join-Path $repo 'artifacts\bin\LCOfficeCleanup.dll'
if(-not(Test-Path -LiteralPath $dll)){throw 'Run Build.cmd first.'}
if($manifest.name -notmatch '^[A-Za-z0-9_]{1,128}$' -or $manifest.version_number -notmatch '^\d+\.\d+\.\d+$' -or [string]::IsNullOrWhiteSpace($manifest.description) -or $manifest.description.Length -gt 250){throw 'Invalid package manifest.'}
foreach($dependency in $manifest.dependencies){if($dependency -notmatch '^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$'){throw "Invalid dependency: $dependency"}}
if($manifest.website_url -and $manifest.website_url -notmatch '^https?://'){throw 'website_url must be empty or an HTTP(S) URL.'}
if(([Reflection.AssemblyName]::GetAssemblyName($dll)).Version.ToString(3) -ne $manifest.version_number){throw 'DLL and manifest versions differ. Rebuild after changing the version.'}
$icon=Join-Path $repo 'package\icon.png'
Add-Type -AssemblyName System.Drawing
$picture=[Drawing.Image]::FromFile($icon)
try{if($picture.Width -ne 256 -or $picture.Height -ne 256){throw 'icon.png must be exactly 256 x 256 pixels.'}}finally{$picture.Dispose()}
$release=Join-Path $repo 'artifacts\releases'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$destination=Join-Path $release ($manifest.name+'-'+$manifest.version_number+'.zip')
$temporary=$destination+'.tmp'
if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$zip=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
try{
 $files=@{'README.md'=(Join-Path $repo 'README.md');'CHANGELOG.md'=(Join-Path $repo 'CHANGELOG.md');'LICENSE.txt'=(Join-Path $repo 'LICENSE.txt');'manifest.json'=$manifestPath;'icon.png'=$icon;'plugins/LCOfficeCleanup.dll'=$dll}
 foreach($name in ($files.Keys | Sort-Object)){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$files[$name],$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}
}finally{$zip.Dispose()}
Move-Item -LiteralPath $temporary -Destination $destination -Force
Write-Host "Created $destination"
