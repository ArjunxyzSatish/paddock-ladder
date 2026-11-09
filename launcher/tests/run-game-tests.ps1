# Starts the real game for each session layout and checks what it actually does.
# Needs a free machine: it takes over the screen and mouse for a few minutes.
#   powershell -File run-game-tests.ps1 -AcRoot "E:\Games\Assetto Corsa" -OutDir C:\temp\shots
param(
  [Parameter(Mandatory = $true)][string]$AcRoot,
  [Parameter(Mandatory = $true)][string]$OutDir,
  [string[]]$Layouts = @('pq', 'p', 'q', 'none'),
  [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$test = Join-Path $here '..\bin\PaddockLadderTest.exe'
if (-not (Test-Path $test)) { throw 'Build the test launcher first: build.ps1 -Test' }
if (Get-Process acs -ErrorAction SilentlyContinue) { throw 'Assetto Corsa is already running. Close it first.' }

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GameWindow {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr w, int c);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr w, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr w, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
  public static void Front(IntPtr w) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); ShowWindow(w, 9); SetForegroundWindow(w); }
  public static void Click(IntPtr w, int x, int y) { var p = new POINT { X = x, Y = y }; ClientToScreen(w, ref p); SetCursorPos(p.X, p.Y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}
'@

function Save-Shot($handle, $path) {
  $rect = New-Object GameWindow+RECT
  [void][GameWindow]::GetClientRect($handle, [ref]$rect)
  $origin = New-Object GameWindow+POINT
  [void][GameWindow]::ClientToScreen($handle, [ref]$origin)
  if ($rect.Right -le 0 -or $rect.Bottom -le 0) { return }
  $bitmap = New-Object System.Drawing.Bitmap $rect.Right, $rect.Bottom
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($origin.X, $origin.Y, 0, 0, $bitmap.Size)
  $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
  $graphics.Dispose(); $bitmap.Dispose()
}

$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Assetto Corsa'
$data = Join-Path $docs 'paddock_ladder'
$report = Join-Path $data 'game_test_report.json'
$raceIni = Join-Path $docs 'cfg\race.ini'
$context = Join-Path $data 'race_context.json'
$app = Join-Path $AcRoot 'apps\lua\PaddockLadderTests'
$backup = Join-Path $OutDir 'race.ini.original'
New-Item -ItemType Directory -Force $OutDir | Out-Null
if (Test-Path $raceIni) { Copy-Item $raceIni $backup -Force }

$failures = New-Object System.Collections.ArrayList
function Assert-That($name, $ok, $detail) {
  if ($ok) { Write-Output "ok    $name" } else { [void]$failures.Add($name); Write-Output "FAIL  $name  ($detail)" }
}

try {
  New-Item -ItemType Directory -Force (Join-Path $app 'src') | Out-Null
  Copy-Item (Join-Path $here 'game\*') $app -Recurse -Force
  Copy-Item (Join-Path $here '..\..\src\steward.lua') (Join-Path $app 'src\steward.lua') -Force
  Copy-Item (Join-Path $here '..\..\src\fault.lua') (Join-Path $app 'src\fault.lua') -Force

  foreach ($layout in $Layouts) {
    Write-Output "--- layout: $layout"
    if (Test-Path $report) { Remove-Item $report -Force }
    & $test --game-test-setup $AcRoot $layout | Write-Output
    if ($LASTEXITCODE -ne 0) { throw "Couldn't prepare layout $layout" }

    $game = Start-Process (Join-Path $AcRoot 'acs.exe') -WorkingDirectory $AcRoot -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $shot = 0
    $done = $false
    while ((Get-Date) -lt $deadline -and -not $done -and -not $game.HasExited) {
      Start-Sleep -Seconds 4
      $game.Refresh()
      if ($game.MainWindowHandle -eq [IntPtr]::Zero) { continue }
      [GameWindow]::Front($game.MainWindowHandle)
      Start-Sleep -Milliseconds 400
      Start-Sleep -Seconds 2
      $shot++
      Save-Shot $game.MainWindowHandle (Join-Path $OutDir ("{0}_{1:00}.jpg" -f $layout, $shot))
      if (Test-Path $report) {
        try { $done = (Get-Content $report -Raw | ConvertFrom-Json).finished -eq $true } catch { }
      }
    }
    if ($done) { Start-Sleep -Seconds 2; Save-Shot $game.MainWindowHandle (Join-Path $OutDir ("{0}_final.jpg" -f $layout)) }
    if (-not $game.HasExited) { Stop-Process -Id $game.Id -Force; $game.WaitForExit(15000) | Out-Null }
    Start-Sleep -Seconds 3

    Assert-That "$layout reached the race" $done 'no finished report before the timeout'
    if (-not $done) { continue }

    $result = Get-Content $report -Raw | ConvertFrom-Json
    Copy-Item $report (Join-Path $OutDir "$layout.report.json") -Force
    $expected = @()
    if ($layout -like '*p*') { $expected += 1 }
    if ($layout -like '*q*') { $expected += 2 }
    $expected += 3
    $types = @($result.sessions | ForEach-Object { $_.type })
    Assert-That "$layout session order" (($types -join ',') -eq ($expected -join ',')) ($types -join ',')
    # With practice on and qualifying off, a hidden qualifying session is skipped before it can be sampled.
    $sessionsInGame = if ($layout -eq 'p') { 3 } else { $expected.Count }
    Assert-That "$layout session count" ($result.sessionsCount -eq $sessionsInGame) $result.sessionsCount
    Assert-That "$layout steward checks" ($result.stewardChecks -ge 11 -and @($result.stewardFailures).Count -eq 0) (@($result.stewardFailures) -join '; ')
    foreach ($session in $result.sessions) {
      Assert-That "$layout $($session.name) window open" ($session.windowOpen -eq $true) 'closed'
    }
    $race = $result.sessions | Where-Object { $_.type -eq 3 }
    if ($layout -notlike '*q*') {
      Assert-That "$layout starts from the back" ($race.position -eq $race.cars) "P$($race.position) of $($race.cars)"
    }
    Assert-That "$layout race length" ($race.laps -eq 1) $race.laps
  }
}
finally {
  if (Get-Process acs -ErrorAction SilentlyContinue) { Stop-Process -Name acs -Force }
  Start-Sleep -Seconds 2
  if (Test-Path $app) { Remove-Item $app -Recurse -Force }
  if (Test-Path $backup) { Copy-Item $backup $raceIni -Force }
  foreach ($file in @($context, $report)) { if (Test-Path $file) { Remove-Item $file -Force } }
}

if ($failures.Count -gt 0) { Write-Output "game tests failed: $($failures.Count)"; exit 1 }
Write-Output 'game tests passed'
