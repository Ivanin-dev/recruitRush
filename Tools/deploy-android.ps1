# Builds the game for Android and installs + launches it on the USB-connected phone.
# Usage:  .\Tools\deploy-android.ps1            (release build)
#         .\Tools\deploy-android.ps1 -Dev       (development build: profiler, logs)
# Close the Unity Editor first: batch mode cannot open a project that is already open.
param([switch]$Dev)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$version = (Select-String "$project\ProjectSettings\ProjectVersion.txt" -Pattern 'm_EditorVersion: (.+)').Matches[0].Groups[1].Value
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
$log = "$project\Logs\android_build.log"

$args = @('-batchmode', '-projectPath', "`"$project`"", '-buildTarget', 'Android',
          '-executeMethod', 'RecruitRush.EditorTools.ProjectSetup.BuildAndroidBatch', '-logFile', "`"$log`"")
if ($Dev) { $args += '-devbuild' }

Write-Host "Building with Unity $version ..."
$p = Start-Process -FilePath $unity -ArgumentList $args -Wait -PassThru
Select-String $log -Pattern 'error CS|\[SETUP\]|CommandInvokationFailure|Exception:' | ForEach-Object { $_.Line }
if ($p.ExitCode -ne 0) { Write-Host "FAILED (exit $($p.ExitCode)). Full log: $log"; exit $p.ExitCode }
Write-Host "Done: installed and launched on the phone."
