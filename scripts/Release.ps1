$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Build.ps1')
& (Join-Path $PSScriptRoot 'Package.ps1')
