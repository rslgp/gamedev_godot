<#
.SYNOPSIS
  Runs a Godot (.NET) project headless with a timeout, kills ONLY its own process tree on timeout,
  and scans the output for engine/C# error patterns (Godot exits 0 after an unhandled C# exception).

.DESCRIPTION
  Dot-source it to get Invoke-GodotHeadless, or run it directly (exit 0 = clean run, 1 = non-zero
  exit code or error lines or a missing -ExpectPattern, 124 = timeout).
  Godot exe: -GodotExe, else $env:GODOT_EXE. Use the *_console.exe build on Windows so errors reach stdout.

.EXAMPLE
  . ./addons/test_harness/tools/run_godot_headless.ps1
  $r = Invoke-GodotHeadless -ProjectPath . -Scene res://tests/component_tests.tscn -UserArgs '--componenttest' -ExpectPattern 'COMPONENTTEST \d+ passed, 0 failed'
  if (-not $r.Ok) { $r.Text; exit 1 }
#>
param(
    [string]$GodotExe = $env:GODOT_EXE,
    [string]$ProjectPath,
    [string]$Scene = '',
    [string[]]$UserArgs = @(),
    [int]$TimeoutSec = 120,
    [switch]$Windowed,
    [string]$ErrorPattern = 'SCRIPT ERROR|Parse Error|Unhandled exception|   at [A-Z]',
    [string]$ExpectPattern = '',
    [string[]]$ExtraGodotArgs = @()
)

function Invoke-GodotHeadless {
    param(
        [string]$GodotExe = $env:GODOT_EXE,
        [Parameter(Mandatory)][string]$ProjectPath,
        [string]$Scene = '',
        [string[]]$UserArgs = @(),
        [int]$TimeoutSec = 120,
        [switch]$Windowed,
        [string]$ErrorPattern = 'SCRIPT ERROR|Parse Error|Unhandled exception|   at [A-Z]',
        [string]$ExpectPattern = '',
        [string[]]$ExtraGodotArgs = @()
    )
    if (-not $GodotExe -or -not (Test-Path -LiteralPath $GodotExe)) { throw 'Godot exe not found: pass -GodotExe or set GODOT_EXE' }
    $proj = (Resolve-Path -LiteralPath $ProjectPath).Path
    function Q([string]$s) { if ($s -match '[\s"]') { '"' + ($s -replace '"', '\"') + '"' } else { $s } }
    $list = @()
    if (-not $Windowed) { $list += '--headless' }
    $list += '--path', (Q $proj)
    $list += $ExtraGodotArgs
    if ($Scene) { $list += (Q $Scene) }
    if ($UserArgs.Count) { $list += '--'; $list += ($UserArgs | ForEach-Object { Q $_ }) }
    $tmp = [IO.Path]::GetTempPath()
    $out = Join-Path $tmp ("godot_run_{0}.out" -f [guid]::NewGuid().ToString('N'))
    $err = "$out.err"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $p = Start-Process -FilePath $GodotExe -ArgumentList $list -WorkingDirectory $proj -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError $err
        $timedOut = -not $p.WaitForExit($TimeoutSec * 1000)
        if ($timedOut) { & taskkill /T /F /PID $p.Id 2>&1 | Out-Null; $code = 124 } else { $p.WaitForExit(); $code = $p.ExitCode }
        $text = ((Get-Content -LiteralPath $out, $err -ErrorAction SilentlyContinue) -join "`n")
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
    $errLines = @($text -split "`n" | Where-Object { $_ -cmatch $ErrorPattern }) # case-sensitive, like [regex]::Matches
    $expectOk = (-not $ExpectPattern) -or ($text -cmatch $ExpectPattern)
    [pscustomobject]@{
        Ok         = (-not $timedOut) -and $code -eq 0 -and $errLines.Count -eq 0 -and $expectOk
        Code       = $code
        TimedOut   = $timedOut
        ErrorLines = $errLines
        ExpectOk   = $expectOk
        Seconds    = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        Text       = $text
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $ProjectPath) { Write-Output 'usage: run_godot_headless.ps1 -ProjectPath <dir> [-Scene res://x.tscn] [-UserArgs ...] [-TimeoutSec n] [-ExpectPattern re]'; exit 2 }
    $r = Invoke-GodotHeadless -GodotExe $GodotExe -ProjectPath $ProjectPath -Scene $Scene -UserArgs $UserArgs -TimeoutSec $TimeoutSec -Windowed:$Windowed -ErrorPattern $ErrorPattern -ExpectPattern $ExpectPattern -ExtraGodotArgs $ExtraGodotArgs
    Write-Output $r.Text
    Write-Output ("RUN code={0} timeout={1} error_lines={2} expect_ok={3} secs={4}" -f $r.Code, $r.TimedOut, $r.ErrorLines.Count, $r.ExpectOk, $r.Seconds)
    exit $(if ($r.TimedOut) { 124 } elseif ($r.Ok) { 0 } else { 1 })
}
