<#
  VinceNoir - batch setup / build / automated tests (Windows PowerShell 5.1+).
  The Unity editor must be CLOSED for this project while it runs (batch mode locks the project).

  Examples
    .\Tools\BuildAndTest.ps1                          # Setup Everything (scenes, lighting bake) + Windows build
    .\Tools\BuildAndTest.ps1 -Step build              # only rebuild the player (code changes)
    .\Tools\BuildAndTest.ps1 -Step setup -Test autotest
    .\Tools\BuildAndTest.ps1 -Step none -Test perftest
    .\Tools\BuildAndTest.ps1 -Step none -Test alleytest

  Tests (run the built player):
    autotest  : the bar - drawing, door, suspicion, alarm, real fire, every wound rule; screenshots + [BARTEST] log
    perftest  : the bar - fixed walk + firefight, one [PERF] line per phase (fps, 1% low, worst frame, GC)
    alleytest : the training alley - run feet, first person, lean, smoking, jacket draws, accuracy, reload walk
  Output goes to TestRuns\<test>_<time>\ (screenshots, player.log).
#>
param(
    [ValidateSet("setup", "build", "none")] [string]$Step = "setup",
    [ValidateSet("none", "autotest", "perftest", "alleytest")] [string]$Test = "none",
    [string]$UnityExe = ""
)
$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $UnityExe) {
    $ver = ((Get-Content (Join-Path $project "ProjectSettings\ProjectVersion.txt") | Select-String "m_EditorVersion:").ToString().Split(":")[1]).Trim()
    $UnityExe = "C:\Program Files\Unity\Hub\Editor\$ver\Editor\Unity.exe"
}
if ($Step -ne "none") {
    if (-not (Test-Path $UnityExe)) { throw "Unity not found at $UnityExe (install the version in ProjectSettings\ProjectVersion.txt via Unity Hub, or pass -UnityExe)" }
    if (Test-Path (Join-Path $project "Temp\UnityLockfile")) { Write-Warning "Temp\UnityLockfile exists - close the Unity editor for this project first." }
    $method = if ($Step -eq "setup") { "VinceSetup.SetupAndBuild" } else { "VinceSetup.BuildPlayer" }
    $logDir = Join-Path $project "TestRuns"; New-Item -ItemType Directory -Force $logDir | Out-Null
    $log = Join-Path $logDir "unity_$Step.log"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $UnityExe -ArgumentList "-batchmode", "-quit", "-projectPath", "`"$project`"", "-executeMethod", $method, "-logFile", "`"$log`"" -PassThru -Wait
    Write-Host "Unity exited $($p.ExitCode) after $([int]$sw.Elapsed.TotalSeconds) s  (log: $log)"
    Select-String -Path $log -Pattern "error CS|\[VINCE\] (build result|setup complete)|\[BAKE\]|\[BAR\]" | ForEach-Object { $_.Line }
}
if ($Test -ne "none") {
    $exe = Join-Path $project "Builds\VinceNoir.exe"
    if (-not (Test-Path $exe)) { throw "No build at $exe - run with -Step setup or -Step build first" }
    $out = Join-Path $project ("TestRuns\{0}_{1:yyyyMMdd_HHmmss}" -f $Test, (Get-Date))
    New-Item -ItemType Directory -Force $out | Out-Null
    $pargs = @("-$Test")
    if ($Test -eq "alleytest") { $pargs += "-autotest" }
    $pargs += @("-shotdir", "`"$out`"", "-logFile", "`"$out\player.log`"", "-screen-width", "1600", "-screen-height", "900", "-screen-fullscreen", "0")
    $q = Start-Process -FilePath $exe -ArgumentList $pargs -PassThru -Wait
    Write-Host "player exited $($q.ExitCode)  ->  $out"
    Select-String -Path "$out\player.log" -Pattern "\[PERF\]|\[BARTEST\] (after|alarm|real|bots fired|head:|heart:|chest:|neck:|belly:|arm:|leg:|vest\d:|done)|\[AUTOTEST\] (accuracy|reload walk|done)|Exception" |
        ForEach-Object { $_.Line }
}
