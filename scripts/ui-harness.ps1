<#
.SYNOPSIS
    UI harness for the RightBTRadio settings window, driven by the winapp CLI.

.DESCRIPTION
    Launches RightBTRadio.WinUI.exe, asserts the properties promised by
    SettingsVisualContract, leaves a screenshot as evidence and closes what it opened.
    Exits 0 when every assertion held, 1 otherwise.

    The expected values are parsed from RightBTRadio.Core/SettingsVisualContract.cs on
    every run instead of being copied here, so a contract change without a matching UI
    change - or the reverse - breaks the harness on purpose.

    Requires an interactive Windows session: winapp ui drives UI Automation and needs a
    real desktop. Only the verbs that do not inject input are used (search, get-property,
    get-value, invoke, list-windows, wait-for); screenshot is evidence, not an assertion,
    and can be skipped.

.PARAMETER Configuration
    Build configuration to exercise. Debug by default.

.PARAMETER Attach
    Measure an already running window instead of launching one, and leave it open.

.PARAMETER KeepOpen
    Leave the application running after the run, to keep looking by hand.

.PARAMETER SkipEvidence
    Skip the screenshot.

.PARAMETER EvidenceDirectory
    Where to leave the screenshot. artifacts/ui-harness by default.

.EXAMPLE
    pwsh -File scripts/ui-harness.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Attach,
    [switch]$KeepOpen,
    [switch]$SkipEvidence,
    [string]$EvidenceDirectory = 'artifacts/ui-harness'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()
$checks = 0

function Write-Section([string]$Text) {
    Write-Host ''
    Write-Host $Text -ForegroundColor Cyan
}

function Assert-Equal([string]$Label, $Expected, $Actual) {
    $script:checks++
    if ($Expected -eq $Actual) {
        Write-Host "  OK   $Label" -ForegroundColor Green
    }
    else {
        Write-Host "  FAIL $Label" -ForegroundColor Red
        Write-Host "       expected: $Expected"
        Write-Host "       actual:   $Actual"
        $failures.Add($Label)
    }
}

function Assert-True([string]$Label, [bool]$Condition, [string]$Detail = '') {
    $script:checks++
    if ($Condition) {
        Write-Host "  OK   $Label" -ForegroundColor Green
    }
    else {
        Write-Host "  FAIL $Label" -ForegroundColor Red
        if ($Detail) { Write-Host "       $Detail" }
        $failures.Add($Label)
    }
}

function Get-Contract {
    <#
        Parses the string and int constants out of SettingsVisualContract.cs. The contract
        is internal to RightBTRadio.Core and cannot be referenced from outside the
        assembly, so parsing it keeps a single source of truth.
    #>
    $path = Join-Path $repositoryRoot 'RightBTRadio.Core/SettingsVisualContract.cs'
    if (-not (Test-Path $path)) {
        throw "Contract not found: $path"
    }

    $source = Get-Content -Raw -LiteralPath $path
    $contract = @{}

    foreach ($match in [regex]::Matches($source, 'public const string (\w+)\s*=\s*((?:"[^"]*"\s*\+?\s*)+);')) {
        $literal = ($match.Groups[2].Value -split '"' | Where-Object { $_ -notmatch '^\s*\+?\s*$' }) -join ''
        $contract[$match.Groups[1].Value] = $literal
    }

    foreach ($match in [regex]::Matches($source, 'public const int (\w+)\s*=\s*(\d+);')) {
        $contract[$match.Groups[1].Value] = [int]$match.Groups[2].Value
    }

    if ($contract.Count -eq 0) {
        throw "No constants parsed from $path"
    }

    return $contract
}

function Get-SettingsExecutable {
    <#
        Two output paths exist because a solution build carries Platform=x64 and a
        project build does not. Taking the newest of the two is what keeps the harness
        from measuring a stale binary, which is exactly how it first produced a green
        run against an old contract.
    #>
    $candidates = @(
        "RightBTRadio.WinUI/bin/$Configuration/net10.0-windows10.0.19041.0/win-x64/RightBTRadio.WinUI.exe",
        "RightBTRadio.WinUI/bin/x64/$Configuration/net10.0-windows10.0.19041.0/RightBTRadio.WinUI.exe",
        "RightBTRadio.WinUI/bin/x64/$Configuration/net10.0-windows10.0.19041.0/win-x64/RightBTRadio.WinUI.exe"
    ) | ForEach-Object { Join-Path $repositoryRoot $_ } | Where-Object { Test-Path $_ }

    if (-not $candidates) {
        throw "Settings window not built for $Configuration."
    }

    return (Get-Item $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Invoke-Winapp {
    <#
        Runs winapp and returns stdout. winapp exits 1 when a search finds nothing or a
        wait times out, which is a result and not a script error, so the exit code is
        handed back through $script:LastWinappExit instead of throwing.
    #>
    param([string[]]$Arguments)

    $output = & winapp @Arguments 2>&1
    $script:LastWinappExit = $LASTEXITCODE
    return ($output | Out-String)
}

function Get-WindowByTitle([string]$Title) {
    $json = Invoke-Winapp @('ui', 'list-windows', '--json')
    if (-not $json) { return $null }

    $parsed = $json | ConvertFrom-Json
    $windows = if ($parsed.PSObject.Properties.Name -contains 'windows') { $parsed.windows } else { $parsed }
    # Not every window reports a title, and StrictMode treats a missing property as an error.
    return $windows |
        Where-Object { $_.PSObject.Properties.Name -contains 'title' -and $_.title -eq $Title } |
        Select-Object -First 1
}

function Find-Element([string]$Window, [string]$Query, [string]$Type) {
    <#
        Resolves by name or AutomationId at the moment of use. Never by slug: the slug
        carries a RuntimeId-derived suffix that changes between runs of the process, and
        winapp rejects a stale one.
    #>
    $json = Invoke-Winapp @('ui', 'search', $Query, '-w', $Window, '--json')
    if ($script:LastWinappExit -ne 0 -or -not $json) { return $null }

    $parsed = $json | ConvertFrom-Json
    $matches = if ($parsed.PSObject.Properties.Name -contains 'matches') { $parsed.matches } else { $parsed }
    if (-not $matches) { return $null }

    if ($Type) {
        # Label and control often share a name, so the type has to disambiguate.
        $matches = $matches | Where-Object { $_.type -eq $Type -or $_.controlType -eq $Type }
    }

    return $matches | Select-Object -First 1
}

function Get-ElementProperty([string]$Window, [string]$Query, [string]$Property) {
    $json = Invoke-Winapp @('ui', 'get-property', $Query, '-w', $Window, '--property', $Property, '--json')
    if ($script:LastWinappExit -ne 0 -or -not $json) { return $null }

    $parsed = $json | ConvertFrom-Json
    foreach ($name in @('value', 'Value', $Property)) {
        if ($parsed.PSObject.Properties.Name -contains $name) { return $parsed.$name }
    }

    return $parsed
}

Add-Type -Namespace RightBTRadioHarness -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern uint GetDpiForWindow(System.IntPtr window);
'@

function Get-WindowDpi([int]$Handle) {
    $dpi = [RightBTRadioHarness.Native]::GetDpiForWindow([System.IntPtr]$Handle)
    if ($dpi -lt 96) { return 96 }
    return $dpi
}

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    Write-Error 'winapp CLI not found. Install the Windows App CLI (winapp) and retry.'
    exit 1
}

$contract = Get-Contract
Write-Section "Contract read from RightBTRadio.Core/SettingsVisualContract.cs ($($contract.Count) constants)"

$process = $null
if (-not $Attach) {
    Write-Section "Building RightBTRadio.WinUI ($Configuration)"
    & dotnet build (Join-Path $repositoryRoot 'RightBTRadio.WinUI/RightBTRadio.WinUI.csproj') `
        -c $Configuration --nologo -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error 'Build failed.'
        exit 1
    }

    $executable = Get-SettingsExecutable

    # Single window per process: a leftover instance would be measured instead of ours.
    Get-Process -Name 'RightBTRadio.WinUI' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300

    Write-Section "Launching $executable"
    $process = Start-Process -FilePath $executable -PassThru
}

$window = $null
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    $window = Get-WindowByTitle $contract.WindowTitle
    if ($window) { break }
    Start-Sleep -Milliseconds 250
}

if (-not $window) {
    Write-Error "The window '$($contract.WindowTitle)' never appeared."
    if ($process -and -not $KeepOpen) { $process | Stop-Process -Force -ErrorAction SilentlyContinue }
    exit 1
}

$handle = $window.hwnd
Write-Host "  window $handle"

Write-Section 'Texts'
Assert-Equal 'window title' $contract.WindowTitle $window.title

$subtitle = Invoke-Winapp @('ui', 'get-value', $contract.SubtitleId, '-w', $handle)
Assert-Equal 'subtitle' $contract.Subtitle $subtitle.Trim()

$devicesDescription = Invoke-Winapp @('ui', 'get-value', $contract.DevicesDescriptionId, '-w', $handle)
Assert-Equal 'devices description' $contract.DevicesDescription $devicesDescription.Trim()

$priorityDescription = Invoke-Winapp @('ui', 'get-value', $contract.PriorityDescriptionId, '-w', $handle)
Assert-Equal 'priority description' $contract.PriorityDescription $priorityDescription.Trim()

Write-Section 'Automation identifiers'
foreach ($id in @(
        $contract.SubtitleId,
        $contract.DevicesListId,
        $contract.PriorityListId,
        $contract.AliasTextBoxId,
        $contract.AddButtonId,
        $contract.RemoveButtonId,
        $contract.MoveUpButtonId,
        $contract.MoveDownButtonId,
        $contract.StartWithWindowsToggleId,
        $contract.StartMinimizedToggleId)) {
    Assert-True "element $id exists" ($null -ne (Find-Element $handle $id ''))
}

Write-Section 'Initial size'
# ResizeForCurrentDpi scales with ceil(logical * dpi / 96), so the promised size only
# equals the raw numbers at 100%. The expected value is recomputed with the real DPI.
$dpi = [int](Get-WindowDpi $handle)

$expectedWidth = [Math]::Ceiling($contract.MinimumWidth * $dpi / 96)
$expectedHeight = [Math]::Ceiling($contract.MinimumHeight * $dpi / 96)
Assert-True 'width at least the promised minimum' ($window.width -ge $expectedWidth - 2) `
    "expected >= $expectedWidth, actual $($window.width)"
Assert-True 'height at least the promised minimum' ($window.height -ge $expectedHeight - 2) `
    "expected >= $expectedHeight, actual $($window.height)"

Write-Section 'General settings'
# The startup switch is read, never actioned: its handler writes the machine's own
# autostart configuration and a harness must not change the station it runs on.
$toggleState = Get-ElementProperty $handle $contract.StartWithWindowsToggleId 'ToggleState'
Assert-True 'start-with-Windows switch readable' ($null -ne $toggleState) "ToggleState = $toggleState"

if (-not $SkipEvidence) {
    Write-Section 'Evidence'
    $directory = Join-Path $repositoryRoot $EvidenceDirectory
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $screenshot = Join-Path $directory ("settings-{0:yyyyMMdd-HHmmss}.png" -f (Get-Date))
    # No selector: with one, winapp captures that element and not the window. --capture-screen
    # goes through the screen DC, which is what shows whether Mica is actually rendering.
    Invoke-Winapp @('ui', 'screenshot', '-w', $handle, '--capture-screen', '--output', $screenshot) | Out-Null
    if (Test-Path $screenshot) {
        Write-Host "  $screenshot"
    }
    else {
        Write-Host '  screenshot not produced' -ForegroundColor Yellow
    }
}

if ($process -and -not $KeepOpen -and -not $Attach) {
    $process | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Section 'Result'
if ($failures.Count -eq 0) {
    Write-Host "  $checks assertions, all held." -ForegroundColor Green
    exit 0
}

Write-Host "  $($failures.Count) of $checks assertions failed:" -ForegroundColor Red
$failures | ForEach-Object { Write-Host "    - $_" }
exit 1
