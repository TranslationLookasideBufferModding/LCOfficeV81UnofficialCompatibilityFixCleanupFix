param([switch]$Test)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$settingsPath=Join-Path $repo 'local.settings.json'
if(-not(Test-Path -LiteralPath $settingsPath)){throw 'Copy local.settings.example.json to local.settings.json and enter your local paths first.'}
$settings=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$core=Join-Path $settings.ProfileDirectory 'BepInEx\core'
$managed=Join-Path $settings.GameDirectory 'Lethal Company_Data\Managed'
$plugins=Join-Path $settings.ProfileDirectory 'BepInEx\plugins'
$bin=Join-Path $repo 'artifacts\bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw 'The Windows .NET Framework C# compiler was not found.'}
$refs=@((Join-Path $core 'BepInEx.dll'),(Join-Path $core '0Harmony.dll'))
foreach($name in @('Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.AIModule.dll','Unity.Netcode.Runtime.dll','netstandard.dll')){$refs+=Join-Path $managed $name}
$dll=Join-Path $bin 'LCOfficeCleanup.dll'
$compile=@('/nologo','/target:library','/optimize+','/langversion:5',"/out:$dll")
$compile+=@($refs | ForEach-Object {if(-not(Test-Path -LiteralPath $_)){throw "Missing reference: $_"};"/reference:$_"})
$compile+=@(Get-ChildItem -LiteralPath (Join-Path $repo 'src') -File -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object {$_.FullName})
& $compiler @compile
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
Write-Host "Built $dll"
if($Test){
 if(-not $settings.UnityEditor -or -not(Test-Path -LiteralPath $settings.UnityEditor)){throw 'Set UnityEditor in local.settings.json to an existing Unity Editor executable.'}
 $output=Join-Path $repo 'artifacts\test-output'
 New-Item -ItemType Directory -Path $output -Force | Out-Null
 $unityLog=Join-Path $output 'Unity.log'
 $project=Join-Path $repo 'tests\UnityHarness'
 $arguments=@('-batchmode','-nographics','-quit','-noUpm','-projectPath',('"'+$project+'"'),'-executeMethod','OfficeCleanupTests.Run','-pluginPath',('"'+$dll+'"'),'-corePath',('"'+$core+'"'),'-managedPath',('"'+$managed+'"'),'-pluginsPath',('"'+$plugins+'"'),'-logFile',('"'+$unityLog+'"'))
 $process=Start-Process -FilePath $settings.UnityEditor -ArgumentList $arguments -PassThru -WindowStyle Hidden
 while(-not $process.WaitForExit(1000)){if((Get-Date)-$process.StartTime -gt [TimeSpan]::FromMinutes(4)){$process.Kill();throw 'Unity checks timed out.'}}
 $process.WaitForExit()
 $process.Refresh()
 $log=Get-Content -LiteralPath $unityLog -Raw
 if($process.ExitCode -ne 0 -or -not $log.Contains('OFFICE_CLEANUP_TESTS_OK')){
  $log -split "`r?`n" | Where-Object {$_ -match 'Exception|error CS|FAIL' -and $_ -notmatch '^\[Licensing'} | ForEach-Object {Write-Host $_}
  throw "Unity checks failed (exit code: $($process.ExitCode)). See $unityLog"
 }
 $log -split "`r?`n" | Where-Object {$_ -match '^PASS|^OFFICE_CLEANUP_TESTS_OK'} | ForEach-Object {Write-Host $_}
}
