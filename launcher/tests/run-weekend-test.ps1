# Plays a race weekend in the real game: damage in practice and qualifying, repairs between them, then quits mid-race.
# Needs a free machine, and the launcher must be closed. Careers are copied first and put back afterwards.
#   powershell -File run-weekend-test.ps1 -AcRoot "E:\Games\Assetto Corsa" -OutDir C:\temp\shots
param(
  [Parameter(Mandatory = $true)][string]$AcRoot,
  [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$test = (Resolve-Path (Join-Path $here '..\bin\PaddockLadderTest.exe')).Path
if (Get-Process acs -ErrorAction SilentlyContinue) { throw 'Assetto Corsa is already running. Close it first.' }
if (Get-Process PaddockLadder, PaddockLadderTest -ErrorAction SilentlyContinue) { throw 'Close Paddock Ladder first.' }

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GameWindow {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr w, int c);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr w, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr w, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
  public static void Front(IntPtr w) { if (GetForegroundWindow() == w) return; keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); ShowWindow(w, 9); SetForegroundWindow(w); }
  delegate bool Visit(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(Visit v, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  public static int CountTitled(uint pid, string a, string b) { int n = 0; EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { var s = new System.Text.StringBuilder(64); GetWindowText(h, s, 64); if (s.ToString() == a || s.ToString() == b) n++; } return true; }, IntPtr.Zero); return n; }
  public static void Move(IntPtr w, int x, int y) { var p = new POINT { X = x, Y = y }; ClientToScreen(w, ref p); SetCursorPos(p.X, p.Y); }
  public static void Press() { mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  public static void Key(byte k, byte scan, int ms) { keybd_event(k, scan, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(ms); keybd_event(k, scan, 2, UIntPtr.Zero); }
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

function Save-Desktop($path) {
  $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
  $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Jpeg)
  $graphics.Dispose(); $bitmap.Dispose()
}

$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Assetto Corsa'
$data = Join-Path $docs 'paddock_ladder'
$raceIni = Join-Path $docs 'cfg\race.ini'
$app = Join-Path $AcRoot 'apps\lua\PaddockLadderWeekendTests'
$saved = Join-Path $OutDir 'saved'

$failures = New-Object System.Collections.ArrayList
function Assert-That($name, $ok, $detail) {
  if ($ok) { Write-Host "ok    $name" } else { [void]$failures.Add($name); Write-Host "FAIL  $name  ($detail)" }
}

function Read-Json($name) {
  $path = Join-Path $data $name
  if (-not (Test-Path $path)) { return $null }
  try { return Get-Content $path -Raw | ConvertFrom-Json } catch { return $null }
}

function Read-Career() {
  return Get-ChildItem (Join-Path $data 'careers') -Filter '*.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json } | Where-Object { $_.Driver -eq 'Weekend Test' } | Select-Object -First 1
}

function Wait-Report($seconds, $handle, [scriptblock]$until) {
  $deadline = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ($handle -ne [IntPtr]::Zero) { [GameWindow]::Front($handle) }
    $report = Read-Json 'weekend_test.json'
    if ($report -and (& $until $report)) { return $report }
  }
  return $null
}

function Click-Point($handle, $x, $y, $shot) {
  [GameWindow]::Front($handle)
  [GameWindow]::Move($handle, [int]$x, [int]$y)
  Start-Sleep -Milliseconds 700
  if ($shot) { Save-Shot $handle (Join-Path $OutDir $shot) }
  [GameWindow]::Press()
  Start-Sleep -Milliseconds 1200
}

function Click-Panel($handle, $dx, $dy, $shot) {
  $rect = New-Object GameWindow+RECT
  [void][GameWindow]::GetClientRect($handle, [ref]$rect)
  $scale = [Math]::Max(0.55, [Math]::Min($rect.Right / 1920.0, $rect.Bottom / 1080.0))
  Click-Point $handle ($rect.Right / 2 + $dx * $scale) ($rect.Bottom / 2 + $dy * $scale) $shot
}

$launcher = $null
New-Item -ItemType Directory -Force $OutDir | Out-Null
if (Test-Path $saved) { Remove-Item $saved -Recurse -Force }
New-Item -ItemType Directory -Force $saved | Out-Null
Copy-Item $data (Join-Path $saved 'data') -Recurse -Force
if (Test-Path $raceIni) { Copy-Item $raceIni (Join-Path $saved 'race.ini') -Force }

try {
  New-Item -ItemType Directory -Force $app | Out-Null
  Copy-Item (Join-Path $here 'game-weekend\*') $app -Recurse -Force
  if (Test-Path (Join-Path $data 'weekend_test.json')) { Remove-Item (Join-Path $data 'weekend_test.json') -Force }

  & $test --weekend-flow-setup $AcRoot | Write-Output
  if ($LASTEXITCODE -ne 0) { throw "Couldn't prepare the test career" }

  $launcher = Start-Process $test -ArgumentList '--auto-start-race' -PassThru
  $deadline = (Get-Date).AddSeconds(150)
  $game = $null
  while ((Get-Date) -lt $deadline -and -not $game) {
    $game = Get-Process acs -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    Start-Sleep -Seconds 1
  }
  Assert-That 'the game opens' ($game -ne $null) 'no window'
  if (-not $game) { throw 'no game' }
  $handle = $game.MainWindowHandle

  # Practice: the clock runs out by itself.
  $report = Wait-Report 240 $handle { param($r) $r.screen -eq 'garage' }
  Assert-That 'the garage opens after practice' ($report -ne $null) ((Read-Json 'weekend_test.json') | ConvertTo-Json -Compress -Depth 3)
  if (-not $report) { throw 'no garage after practice' }
  Assert-That 'going back to the pits does not repair the car for free' ($report.bodyAfterWipe -ge 39) $report.bodyAfterWipe
  Assert-That 'all contact rulings are right in the game' ($report.faultCases -eq 9 -and @($report.faultFailures).Count -eq 0) ($report.faultFailures -join '; ')
  Start-Sleep -Seconds 3
  Save-Shot $handle (Join-Path $OutDir 'practice_garage.jpg')
  $debrief = Read-Json 'race_debrief.json'
  $career = Read-Career
  Assert-That 'practice damage is on the career' ($career.CarBody[0] -ge 39 -and $career.CarEngine -le 851) "body $($career.CarBody[0]), engine $($career.CarEngine)"
  Assert-That 'the game lists each part with its price' ($debrief.SessionReview -and @($debrief.Parts).Count -eq 2) ($debrief | ConvertTo-Json -Compress -Depth 3)
  $credits = $career.Credits

  # Open the bodywork, take half, leave the engine.
  Click-Panel $handle 595 -284 'practice_row_hover.jpg'
  Start-Sleep -Seconds 1
  Save-Shot $handle (Join-Path $OutDir 'practice_part.jpg')
  Click-Panel $handle 380 4 'practice_half_hover.jpg'
  Start-Sleep -Seconds 2
  $after = Read-Json 'race_debrief.json'
  $career = Read-Career
  Assert-That 'half a body repair from the game' ($career.CarBody[0] -le 21 -and $career.CarBody[0] -ge 19 -and $career.CarEngine -le 851) "body $($career.CarBody[0]), engine $($career.CarEngine)"
  Assert-That 'the half repair was charged' ($career.Credits -lt $credits -and $after.CreditsBalance -eq $career.Credits) "$credits to $($career.Credits)"
  Save-Shot $handle (Join-Path $OutDir 'practice_after_half.jpg')
  Click-Panel $handle 490 374 'practice_continue_hover.jpg'

  # Qualifying: what was left is on the car, and the Skip Session button goes to the garage first.
  $report = Wait-Report 120 $handle { param($r) $r.stage -eq 'qualifying-damaged' -and $r.skipX }
  Assert-That 'qualifying starts after the garage' ($report -ne $null) ((Read-Json 'weekend_test.json') | ConvertTo-Json -Compress -Depth 3)
  if (-not $report) { throw 'no qualifying' }
  Assert-That 'the half-repaired body reached qualifying' ($report.qualifyingBody -ge 19 -and $report.qualifyingBody -le 22) $report.qualifyingBody
  Assert-That 'the unrepaired engine reached qualifying' ($report.qualifyingEngine -le 851) $report.qualifyingEngine
  Assert-That 'the session clock kept the app window up' ($report.windowVisible -eq $true) $report.windowVisible
  Save-Shot $handle (Join-Path $OutDir 'qualifying_track.jpg')
  Start-Sleep -Seconds 2
  Click-Point $handle $report.skipX $report.skipY 'skip_hover.jpg'
  $report = Wait-Report 20 $handle { param($r) $r.screen -eq 'garage' }
  Assert-That 'skip session goes to the garage first' ($report -ne $null) ((Read-Json 'weekend_test.json') | ConvertTo-Json -Compress -Depth 3)
  if ($report) {
    Start-Sleep -Seconds 2
    Save-Shot $handle (Join-Path $OutDir 'qualifying_garage.jpg')
    $career = Read-Career
    Assert-That 'qualifying damage is on the career' ($career.CarBody[1] -ge 29) "$($career.CarBody -join ',')"
    Click-Panel $handle 490 374 'qualifying_continue_hover.jpg'
  }

  # The race: damage carries in, then the player pauses and quits.
  $report = Wait-Report 120 $handle { param($r) $r.stage -eq 'race-damaged' -and $r.winX }
  if ($report) {
    $w = $report.winScale
    Click-Point $handle ($report.winX + $report.winW - 28 * $w) ($report.winY + 28 * $w) 'overlay_car_hover.jpg'
    Start-Sleep -Seconds 1
    Save-Shot $handle (Join-Path $OutDir 'overlay_car.jpg')
    Click-Point $handle ($report.winX + $report.winW / 2) ($report.winY + 8 * $w) $null
    Start-Sleep -Seconds 1
    Save-Shot $handle (Join-Path $OutDir 'overlay_minimised.jpg')
    Click-Point $handle ($report.winX + 60 * $w) ($report.winY + 22 * $w) $null
    Start-Sleep -Seconds 1
    Save-Shot $handle (Join-Path $OutDir 'overlay_restored.jpg')
    Start-Sleep -Seconds 1
    Click-Point $handle ($report.winX + $report.winW - 136 * $w) ($report.winY + 28 * $w) $null
  }
  $report = Wait-Report 120 $handle { param($r) $r.stage -eq 'ready-to-quit' }
  Assert-That 'the race starts and the car can move' ($report -ne $null -and $report.raceSpeed -gt 5) ((Read-Json 'weekend_test.json') | ConvertTo-Json -Compress -Depth 3)
  if (-not $report) { throw 'no race' }
  Assert-That 'left-over damage reached the race' ($report.raceBody[0] -ge 19 -and $report.raceBody[1] -ge 29 -and $report.raceEngine -le 851) "$($report.raceBody -join ','), engine $($report.raceEngine)"
  Save-Shot $handle (Join-Path $OutDir 'race_track.jpg')
  $creditsBefore = (Read-Career).Credits

  Start-Sleep -Seconds 3
  Save-Shot $handle (Join-Path $OutDir 'race_paused.jpg')
  $report = Read-Json 'weekend_test.json'
  $progress = Read-Json 'race_progress.json'
  Assert-That 'the game is paused' ($report.paused -eq $true) $report.paused
  Assert-That 'the race is known to be under way' ($progress.RaceStarted -eq $true) ($progress | ConvertTo-Json -Compress -Depth 2)
  Assert-That 'progress holds the race damage' ($progress.Body[0] -ge 44 -and $progress.EngineLife -le 701) "$($progress.Body -join ','), engine $($progress.EngineLife)"
  Write-Host "info  timeline:"; @($report.timeline) | ForEach-Object { Write-Host "      $_" }

  # The Exit button on the game's own pause menu, which is how a player quits.
  $rect = New-Object GameWindow+RECT
  [void][GameWindow]::GetClientRect($handle, [ref]$rect)
  Click-Point $handle ($rect.Right / 2 + 235 * ($rect.Bottom / 1080.0)) ($rect.Bottom * 0.535) 'exit_hover.jpg'
  $deadline = (Get-Date).AddSeconds(4)
  while ((Get-Date) -lt $deadline -and -not $game.HasExited) { Start-Sleep -Milliseconds 500; $game.Refresh() }
  if (-not $game.HasExited) {
    # The game asks whether you're sure. Leave is on the left of that box.
    Click-Point $handle ($rect.Right / 2 - 95 * ($rect.Bottom / 1080.0)) ($rect.Bottom * 0.54) 'exit_leave_hover.jpg'
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and -not $game.HasExited) { Start-Sleep -Milliseconds 500; $game.Refresh() }
    if ($game.HasExited) { Write-Host 'info  Exit then Leave closed the game' }
  }
  if (-not $game.HasExited) { Write-Host 'info  the Exit button did not close the game, closing the window instead'; [void]$game.CloseMainWindow() }
  $deadline = (Get-Date).AddSeconds(20)
  while ((Get-Date) -lt $deadline -and -not $game.HasExited) { Start-Sleep -Milliseconds 500; $game.Refresh() }
  $closedCleanly = $game.HasExited
  if (-not $closedCleanly) { Write-Host 'info  the window did not close by itself'; Stop-Process -Name acs -Force }
  else { Write-Host "info  the game's exit code was $($game.ExitCode)" }
  $watch = [Diagnostics.Stopwatch]::StartNew()
  $career = $null
  while ($watch.Elapsed.TotalSeconds -lt 15) {
    Start-Sleep -Milliseconds 300
    $career = Read-Career
    if ($career.LastReport -and $career.LastReport.Retired) { break }
  }
  $retiredAfter = $watch.Elapsed.TotalSeconds
  Write-Host ("info  the launcher scored the retirement after {0:0.0}s" -f $retiredAfter)
  foreach ($wait in 2, 4, 4) {
    Start-Sleep -Seconds $wait
    $front = [GameWindow]::GetForegroundWindow()
    $owner = Get-Process | Where-Object { $_.MainWindowHandle -eq $front } | Select-Object -First 1
    Write-Host "info  in front after +$wait s: $($owner.ProcessName) / $($owner.MainWindowTitle)"
  }
  Save-Desktop (Join-Path $OutDir 'retired_launcher.jpg')
  Assert-That 'quitting the race retires you' ($career.LastReport.Retired -eq $true -and $career.Pending -eq $null -and $career.Season.RoundIndex -eq 1) ($career.LastReport | ConvertTo-Json -Compress -Depth 2)
  Assert-That 'race damage is on the career after quitting' ($career.CarBody[0] -ge 44 -and $career.CarEngine -le 701) "$($career.CarBody -join ','), engine $($career.CarEngine)"
  Assert-That 'the launcher reports the retirement within a few seconds' ($retiredAfter -lt 8) $retiredAfter
  Assert-That 'the retirement shows a credit line' (@($career.LastReport.CreditLines).Count -ge 1) ($career.LastReport.CreditLines | ConvertTo-Json -Compress)
  Write-Host "info  credits $creditsBefore to $($career.Credits), trust change $($career.LastReport.TrustDelta)"
  Start-Sleep -Seconds 8
  $cm = Get-Process 'Content Manager' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($cm) {
    $left = [GameWindow]::CountTitled([uint32]$cm.Id, 'Race', 'Cancelled')
    Assert-That 'no Content Manager result windows are left open' ($left -eq 0) $left
  }
}
finally {
  if (Get-Process acs -ErrorAction SilentlyContinue) { Stop-Process -Name acs -Force }
  if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force }
  Start-Sleep -Seconds 2
  $problems = @(Select-String -Path (Join-Path $docs 'logs\custom_shaders_patch.log') -Pattern 'PaddockLadder\] .*(stopped|could not)' | ForEach-Object { $_.Line })
  Assert-That 'the game log has no Paddock Ladder errors' ($problems.Count -eq 0) ($problems -join ' | ')
  if (Test-Path $app) { Remove-Item $app -Recurse -Force }
  if (Test-Path (Join-Path $saved 'race.ini')) { Copy-Item (Join-Path $saved 'race.ini') $raceIni -Force }
  Remove-Item (Join-Path $data 'careers') -Recurse -Force
  Copy-Item (Join-Path $saved 'data\careers') (Join-Path $data 'careers') -Recurse -Force
  Copy-Item (Join-Path $saved 'data\launcher.json') (Join-Path $data 'launcher.json') -Force
  foreach ($name in 'next_race.ini', 'race_context.json', 'race_result.json', 'race_debrief.json', 'race_command.json', 'race_progress.json', 'weekend_test.json') {
    $file = Join-Path $data $name
    if (Test-Path $file) { Remove-Item $file -Force }
  }
  Remove-Item $saved -Recurse -Force
}

if ($failures.Count -gt 0) { Write-Output "weekend test failed: $($failures.Count)"; exit 1 }
Write-Output 'weekend test passed'
