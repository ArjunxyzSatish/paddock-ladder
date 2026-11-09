# Builds PaddockLadder.exe with the C# compiler included in Windows. Pass -Test for the self-test build.
param([switch]$Test)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$project = Split-Path $here -Parent
$fx = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$out = if ($Test) { Join-Path $here 'bin\PaddockLadderTest.exe' } else { Join-Path $project 'dist\PaddockLadder.exe' }
New-Item -ItemType Directory -Force (Split-Path $out -Parent) | Out-Null

$refs = @("$fx\WPF\PresentationCore.dll", "$fx\WPF\PresentationFramework.dll", "$fx\WPF\WindowsBase.dll", "$fx\System.Xaml.dll",
  "$fx\System.Web.Extensions.dll", "$fx\System.Windows.Forms.dll", "$fx\System.Core.dll") | ForEach-Object { "/reference:$_" }

$resources = @(
  "/resource:$project\manifest.ini,game/manifest.ini",
  "/resource:$project\PaddockLadder.lua,game/PaddockLadder.lua",
  "/resource:$project\icon.png,game/icon.png",
  "/resource:$project\music\menu.mp3,game/music/menu.mp3",
  "/resource:$project\sounds\hover.wav,game/sounds/hover.wav",
  "/resource:$project\sounds\click.wav,game/sounds/click.wav",
  "/resource:$project\sounds\scroll.wav,game/sounds/scroll.wav",
  "/resource:$project\sounds\confirm.wav,game/sounds/confirm.wav",
  "/resource:$project\src\steward.lua,game/src/steward.lua",
  "/resource:$project\src\fault.lua,game/src/fault.lua"
)

$sources = @(Get-ChildItem "$here\src\*.cs" | ForEach-Object { $_.FullName })
$options = @('/nologo', '/optimize+', "/out:$out", "/win32icon:$here\icon.ico")
if ($Test) {
  $sources += @(Get-ChildItem "$here\tests\*.cs" | ForEach-Object { $_.FullName })
  $options += @('/target:exe', '/define:SELFTEST')
} else {
  $options += '/target:winexe'
}

& "$fx\csc.exe" @options @refs @resources @sources
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
Write-Output "Built $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
