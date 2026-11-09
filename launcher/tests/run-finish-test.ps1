# Plays the last two rounds of a season in the real game, through the real launcher and Content Manager.
# The car drives itself to the flag. This script then holds the throttle, presses Escape and clicks the results screen.
# Needs a free machine, and the launcher must be closed. Careers are copied first and put back afterwards.
#   powershell -File run-finish-test.ps1 -AcRoot "E:\Games\Assetto Corsa" -OutDir C:\temp\shots
param(
  [Parameter(Mandatory = $true)][string]$AcRoot,
  [Parameter(Mandatory = $true)][string]$OutDir,
  [int]$RoundTimeoutSeconds = 300
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
  [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int Size, Flags; public IntPtr Cursor; public POINT Position; }
  [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO info);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr w, int c);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr w, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr w, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
  public static bool CursorShowing() { var info = new CURSORINFO { Size = Marshal.SizeOf(typeof(CURSORINFO)) }; GetCursorInfo(ref info); return (info.Flags & 1) != 0; }
  /* A bare Alt tap on the window already in front opens menu mode, which swallows the next click. */ public static void Front(IntPtr w) { if (GetForegroundWindow() == w) return; keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); ShowWindow(w, 9); SetForegroundWindow(w); }
  public static void Move(IntPtr w, int x, int y) { var p = new POINT { X = x, Y = y }; ClientToScreen(w, ref p); SetCursorPos(p.X, p.Y); }
  public static void Press() { mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(80); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
  public static void Key(byte k, int ms) { keybd_event(k, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(ms); keybd_event(k, 0, 2, UIntPtr.Zero); }
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
$app = Join-Path $AcRoot 'apps\lua\PaddockLadderFinishTests'
$saved = Join-Path $OutDir 'saved'

$failures = New-Object System.Collections.ArrayList
function Assert-That($name, $ok, $detail) {
  if ($ok) { Write-Host "ok    $name" } else { [void]$failures.Add($name); Write-Host "FAIL  $name  ($detail)" }
}

function Read-Report($round) {
  $path = Join-Path $data "finish_test_$round.json"
  if (-not (Test-Path $path)) { return $null }
  try { return Get-Content $path -Raw | ConvertFrom-Json } catch { return $null }
}

function Wait-Game($seconds) {
  $deadline = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $deadline) {
    $game = Get-Process acs -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    if ($game) { return $game }
    Start-Sleep -Seconds 1
  }
  return $null
}

function Wait-GameGone($seconds) {
  $deadline = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process acs -ErrorAction SilentlyContinue)) { return $true }
    Start-Sleep -Milliseconds 500
  }
  return $false
}

# Runs one round to the results screen and proves the car can't be driven any more.
function Play-Round($round, $game) {
  $handle = $game.MainWindowHandle
  $deadline = (Get-Date).AddSeconds($RoundTimeoutSeconds)
  $report = $null
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $game.Refresh()
    if ($game.HasExited) { break }
    [GameWindow]::Front($handle)
    $report = Read-Report $round
    if ($report -and $report.stage -eq 'locked' -and $report.debrief) { break }
  }
  Assert-That "round $round reached the results screen" ($report -and $report.stage -eq 'locked' -and $report.debrief) ($report | ConvertTo-Json -Compress)
  if (-not ($report -and $report.stage -eq 'locked')) { return $false }

  Start-Sleep -Seconds 1
  Save-Shot $handle (Join-Path $OutDir "round${round}_results.jpg")
  [GameWindow]::Key(0x26, 2500)
  [GameWindow]::Key(0x1B, 100)
  Start-Sleep -Seconds 2
  Save-Shot $handle (Join-Path $OutDir "round${round}_after_escape.jpg")
  [GameWindow]::Key(0x1B, 100)
  Start-Sleep -Seconds 1
  [GameWindow]::Key(0x26, 2500)
  Start-Sleep -Seconds 1

  $report = Read-Report $round
  Assert-That "round $round car is in its pit box" ($report.inPit -eq $true) $report.inPit
  Assert-That "round $round throttle and Escape can't move the car" ($report.maxSpeedLocked -lt 8) $report.maxSpeedLocked
  $stillOpen = -not $game.HasExited
  Assert-That "round $round Escape doesn't close the results" $stillOpen 'the game closed'
  Assert-That "round $round music is playing on the results screen" ($report.musicTime -gt 3) $report.musicTime

  $debrief = Read-Debrief
  Assert-That "round $round credits are shown" (@($debrief.CreditLines).Count -gt 0 -and $debrief.CreditsNet -gt 0) ($debrief.CreditLines | ConvertTo-Json -Compress)
  Assert-That "round $round results start on their own screen" ($report.screen -eq 'result') $report.screen
  if ($round -eq 5) {
    Assert-That 'carried body damage reached the race' ($report.carriedBody -ge 25) $report.carriedBody
    Assert-That 'carried engine wear reached the race' ($report.carriedEngine -le 805) $report.carriedEngine
    Write-Host "info  steering with carried suspension damage: now $($report.steerNow), max $($report.steerMax)"
    Assert-That 'grudges switch on near the player and not elsewhere' ($report.rivalsAngry -ge 1 -and $report.rivalsCalm -ge 1) "angry $($report.rivalsAngry), calm $($report.rivalsCalm)"
    Assert-That 'a repair is offered' ($debrief.RepairCost -gt 0 -and $debrief.CanAffordRepair) $debrief.RepairCost
    Advance-To $handle $round 'car' | Out-Null
    Start-Sleep -Seconds 1
    Save-Shot $handle (Join-Path $OutDir 'round5_car.jpg')
    Click-Until $handle 595 -284 'round5_options_hover.jpg' 5 'part' | Out-Null
    Start-Sleep -Seconds 1
    Save-Shot $handle (Join-Path $OutDir 'round5_part.jpg')
    Click-Until $handle 380 88 'round5_leave_hover.jpg' 5 'car' | Out-Null
    Click-Panel $handle -280 80 'round5_repair_hover.jpg'
    Start-Sleep -Seconds 2
    $after = Read-Debrief
    Assert-That 'repairing in the game clears the bill' ($after.RepairCost -eq 0) $after.RepairCost
    Save-Shot $handle (Join-Path $OutDir 'round5_repaired.jpg')
  } else {
    Assert-That 'a repaired car starts the next race clean' ($report.carriedBody -lt 1 -and $report.carriedEngine -ge 999) "body $($report.carriedBody), engine $($report.carriedEngine)"
    Write-Host "info  steering after repair: now $($report.steerNow), max $($report.steerMax)"
  }
  if ($round -eq 5) {
    Advance-To $handle $round 'next' | Out-Null
    Start-Sleep -Seconds 2
    Save-Shot $handle (Join-Path $OutDir 'round5_next_race.jpg')
    Click-Until $handle 530 372 'round5_load_hover.jpg' $round 'leaving' | Out-Null
  } else {
    Advance-To $handle $round 'offers' | Out-Null
  }
  return $true
}

# Presses the main button repeatedly, photographing each step, until the game reaches the wanted screen.
function Advance-To($handle, $round, $screen) {
  foreach ($attempt in 1..10) {
    Start-Sleep -Seconds 2
    $report = Read-Report $round
    if ($report -and $report.screen -eq $screen) { return $true }
    if ($report) { Save-Shot $handle (Join-Path $OutDir "round${round}_step_$($report.screen).jpg") }
    Click-Panel $handle 530 372 $null
    Start-Sleep -Milliseconds 700
  }
  Assert-That "the game reached $screen" $false ((Read-Report $round) | ConvertTo-Json -Compress)
  return $false
}

# Clicks until the game shows the screen expected next, and says so if a click was missed.
function Click-Until($handle, $dx, $dy, $shot, $round, $screen) {
  foreach ($attempt in 1..3) {
    Click-Panel $handle $dx $dy $shot
    $deadline = (Get-Date).AddSeconds(3)
    while ((Get-Date) -lt $deadline) {
      $report = Read-Report $round
      if ($report -and $report.screen -match "^($screen)$") {
        if ($attempt -gt 1) { Write-Host "info  needed $attempt clicks to reach $screen" }
        return $true
      }
      Start-Sleep -Milliseconds 200
    }
  }
  Assert-That "the game reached $screen" $false ((Read-Report $round) | ConvertTo-Json -Compress)
  return $false
}

# Clicks a point given relative to the centre of the screen, at the layout's full size.
function Click-Panel($handle, $dx, $dy, $shot) {
  $rect = New-Object GameWindow+RECT
  [void][GameWindow]::GetClientRect($handle, [ref]$rect)
  $scale = [Math]::Max(0.55, [Math]::Min($rect.Right / 1920.0, $rect.Bottom / 1080.0))
  [GameWindow]::Front($handle)
  [GameWindow]::Move($handle, [int]($rect.Right / 2 + $dx * $scale), [int]($rect.Bottom / 2 + $dy * $scale))
  Start-Sleep -Milliseconds 700
  if ($shot) { Save-Shot $handle (Join-Path $OutDir $shot) }
  [GameWindow]::Press()
  Start-Sleep -Milliseconds 1200
}

function Read-Debrief() {
  try { return Get-Content (Join-Path $data 'race_debrief.json') -Raw | ConvertFrom-Json } catch { return $null }
}

$launcher = $null
New-Item -ItemType Directory -Force $OutDir | Out-Null
if (Test-Path $saved) { Remove-Item $saved -Recurse -Force }
New-Item -ItemType Directory -Force $saved | Out-Null
Copy-Item $data (Join-Path $saved 'data') -Recurse -Force
if (Test-Path $raceIni) { Copy-Item $raceIni (Join-Path $saved 'race.ini') -Force }

try {
  New-Item -ItemType Directory -Force $app | Out-Null
  Copy-Item (Join-Path $here 'game-finish\*') $app -Recurse -Force

  & $test --finish-flow-setup $AcRoot | Write-Output
  if ($LASTEXITCODE -ne 0) { throw "Couldn't prepare the test career" }

  $launcher = Start-Process $test -ArgumentList '--auto-start-race' -PassThru
  $game = Wait-Game 120
  Assert-That 'the launcher starts round 5' ($game -ne $null) 'no game window'
  if ($game -and (Play-Round 5 $game)) {
    Assert-That 'next race closes the game' (Wait-GameGone 30) 'still open'
    Start-Sleep -Seconds 2
    Save-Desktop (Join-Path $OutDir 'between_rounds.jpg')
    $second = Wait-Game 120
    Assert-That 'next race loads round 6' ($second -ne $null) 'no game window'
    if ($second -and (Play-Round 6 $second)) {
      $handle = $second.MainWindowHandle
      Start-Sleep -Seconds 1
      Save-Shot $handle (Join-Path $OutDir 'offers.jpg')
      Assert-That 'the game stays open for the offers' (-not $second.HasExited) 'the game closed'
      $before = Read-Debrief
      Assert-That 'offers reached the game' ($before.CanChoose -and @($before.Offers).Count -gt 0) ($before | ConvertTo-Json -Compress -Depth 2)

      Click-Until $handle 550 374 'offers_hover.jpg' 6 'seats' | Out-Null
      Save-Shot $handle (Join-Path $OutDir 'seats.jpg')
      Click-Until $handle 615 -225 'seats_hover.jpg' 6 'modal' | Out-Null
      Save-Shot $handle (Join-Path $OutDir 'seat_confirm.jpg')
      Click-Panel $handle 218 88 'seat_confirm_hover.jpg'
      Start-Sleep -Seconds 2
      Save-Shot $handle (Join-Path $OutDir 'seat_answer.jpg')
      $after = Read-Debrief
      Assert-That 'a seat request works in the game' ($after.Revision -ge 1 -and $after.ReplyTitle -ne '') ($after | ConvertTo-Json -Compress -Depth 2)
      Click-Until $handle 218 88 'seat_answer_hover.jpg' 6 'offers' | Out-Null
      Start-Sleep -Seconds 1
      Save-Shot $handle (Join-Path $OutDir 'offers_after_request.jpg')

      $soundReport = Read-Report 6
      Assert-That 'interface sounds played on the in-game screens' ($soundReport.soundHeard -eq 1 -and $soundReport.soundCount -ge 6) ($soundReport | ConvertTo-Json -Compress)
      Click-Until $handle -580 268 'view_car_hover.jpg' 6 'cardetail' | Out-Null
      Start-Sleep -Seconds 1
      Save-Shot $handle (Join-Path $OutDir 'car_detail.jpg')
      Click-Until $handle 530 372 'sign_hover.jpg' 6 'next' | Out-Null
      Start-Sleep -Seconds 2
      Save-Shot $handle (Join-Path $OutDir 'signed_briefing.jpg')
      Click-Panel $handle 530 372 'load_hover.jpg'
      Assert-That 'loading the race closes the game' (Wait-GameGone 30) 'still open'
      Start-Sleep -Seconds 2
      $third = Wait-Game 120
      Assert-That 'signing loads round 1' ($third -ne $null) 'no game window'
      # The new season's first track can be a big one, so it gets longer to load.
      $deadline = (Get-Date).AddSeconds(200)
      $first = $null
      while ((Get-Date) -lt $deadline -and -not ($first -and $first.stage -ne 'wait')) {
        Start-Sleep -Seconds 2
        $first = Read-Report 1
        if ($third -and -not $third.HasExited -and $third.MainWindowHandle -ne [IntPtr]::Zero) { [GameWindow]::Front($third.MainWindowHandle) }
      }
      Assert-That 'round 1 of the new season is on track' ($first -ne $null -and $first.round -eq 1 -and $first.stage -ne 'wait') ($first | ConvertTo-Json -Compress)
      Start-Sleep -Seconds 4
      if ($third -and -not $third.HasExited) { Save-Shot $third.MainWindowHandle (Join-Path $OutDir 'practice_goal.jpg') }
      $career = Get-ChildItem (Join-Path $data 'careers') -Filter '*.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json } | Where-Object { $_.Driver -eq 'Finish Test' } | Select-Object -First 1
      Assert-That 'the old season was scored and the new one signed' ($career.Season -ne $null -and $career.SeasonNo -eq 2 -and $career.Stats.Races -eq 6 -and $career.Season.RoundIndex -eq 0) "season $($career.SeasonNo), races $($career.Stats.Races)"
    }
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
  foreach ($name in 'next_race.ini', 'race_context.json', 'race_result.json', 'race_debrief.json', 'race_command.json', 'finish_test_1.json', 'finish_test_5.json', 'finish_test_6.json') {
    $file = Join-Path $data $name
    if (Test-Path $file) { Remove-Item $file -Force }
  }
  Remove-Item $saved -Recurse -Force
}

if ($failures.Count -gt 0) { Write-Output "finish test failed: $($failures.Count)"; exit 1 }
Write-Output 'finish test passed'
