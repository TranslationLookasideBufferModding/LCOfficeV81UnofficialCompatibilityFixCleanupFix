param([Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference='Stop'
$parts=$Version.Split('.')
foreach($part in $parts){if([int]$part -gt 65534){throw 'Each version component must be at most 65534.'}}
$repo=Split-Path -Parent $PSScriptRoot
$source=Join-Path $repo 'src\Plugin.cs'
$text=[IO.File]::ReadAllText($source)
$text=[regex]::Replace($text,'AssemblyVersion\("\d+\.\d+\.\d+\.0"\)','AssemblyVersion("'+$Version+'.0")')
$text=[regex]::Replace($text,'AssemblyFileVersion\("\d+\.\d+\.\d+\.0"\)','AssemblyFileVersion("'+$Version+'.0")')
$text=[regex]::Replace($text,'BepInPlugin\(Id, "LC Office Cleanup", "\d+\.\d+\.\d+"\)','BepInPlugin(Id, "LC Office Cleanup", "'+$Version+'")')
$text=[regex]::Replace($text,'LC Office Cleanup \d+\.\d+\.\d+ active:','LC Office Cleanup '+$Version+' active:')
[IO.File]::WriteAllText($source,$text,[Text.UTF8Encoding]::new($false))
$manifestPath=Join-Path $repo 'package\manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.version_number=$Version
[IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 5)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
$readme=Join-Path $repo 'README.md'
$readmeText=[regex]::Replace([IO.File]::ReadAllText($readme),'LC Office Cleanup \d+\.\d+\.\d+ active','LC Office Cleanup '+$Version+' active')
[IO.File]::WriteAllText($readme,$readmeText,[Text.UTF8Encoding]::new($false))
Write-Host "Set version to $Version. Update CHANGELOG.md, rebuild, and commit before creating a matching Git tag."
