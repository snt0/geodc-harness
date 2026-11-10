#Requires -Version 7
<#
.SYNOPSIS
    Captures a full-window screenshot of every enumerated geoDC view into a labelled corpus.

.DESCRIPTION
    Launches the app under the UI driver against the dev fixture (dev/fixtures/probe.gdcproj)
    and writes one PNG per view to <OutDir>/<Label>/<view-id>.png, plus a capture.json
    recording fixture fingerprint, app commit and capture timestamp.

    A view can also declare Target = 'sample', which captures the geoDC.App.UI.Sample
    toolkit gallery instead. Views are grouped by target so each one is launched and
    maximised exactly once. The gallery opens no project, so the fixture fingerprint is
    meaningless for its views — capture.json scopes the fingerprint to app views and says
    so, rather than recording a provenance that does not apply.

    The corpus is a *visual baseline*: something to eyeball before and after a UI change
    that touches shared chrome. It is deliberately not a regression guard — see
    "Sources of variance" below.

    The app is launched in the UI driver's passive mode (`attach --launch --passive`): its
    windows open behind whatever you are working in and never take the foreground, and
    the UI driver hands the foreground back if anything pulls the app forward anyway.
    Screenshots use PrintWindow, which captures occluded windows, so the run can proceed
    while you keep typing elsewhere. Pass -Active to launch in active mode instead, where
    the app may come forward and you can click it to watch the run.

    Navigation is click/select only. `geodc-ui key` needs the machine's foreground and fails
    on a locked or disconnected session, which would make this script unrunnable
    unattended.

    stdout/stderr discipline matches dev/ui.ps1: stdout carries exactly one line, a
    JSON run summary; all progress and diagnostics go to stderr.

.PARAMETER Set
    Which declared views to capture. 'spine' (default) is the ten-view backbone: the eight
    shell pages plus the Calibration and Verification Results tabs — app target only, so it
    never pays for a second launch. 'full' adds the sub-views that are addressable today —
    the Survey nav-rail sections, the GNSS Results tab, the two remaining Calibration
    Results presentations, the Data Analysis popup, and the toolkit gallery's sections.
    Deliberately not exhaustive: a sub-view earns a place here when it has an addressable
    identity and shows something a sibling doesn't.

.PARAMETER OutDir
    Corpus root. Defaults to dev/captures (gitignored along with the rest of dev/).

.PARAMETER Label
    Names the corpus directory — what build this is a picture of. Defaults to the short
    commit hash, suffixed '-dirty' when the working tree has uncommitted changes.

.PARAMETER KeepRunning
    Leave the app running after the capture instead of killing the leased instance.

.PARAMETER NoMaximize
    Capture at whatever window size the app restores, instead of maximising first.
    Maximising is the default because most pages scroll: at the app's restored size a
    third of the Calibration data-input flow and half the Survey selected-feature panel
    are below the fold and simply absent from the corpus.

    In the default passive mode "maximising" resizes the window to its monitor's work
    area without activating it: Windows has no show command that maximises without
    activating. The window state stays Normal, so the caption shows the maximise glyph
    rather than restore, and the frame keeps its restored-window border. With -Active
    the window is truly maximised, which brings it to the front.

.PARAMETER Active
    Launch the app in the UI driver's active mode and truly maximise it, which brings it to
    the front, instead of running it passively behind your windows.

.PARAMETER NoElide
    Re-run every declared navigation step even when the previous view just executed the same
    leading steps. Slower, and only worth reaching for when a capture looks like state has
    bled between views.

.PARAMETER InjectBrokenView
    Appends a view whose navigation targets an AutomationId that does not exist. Used to
    demonstrate that an unreachable view fails the run loudly and by name, rather than
    silently producing a corpus with a missing file.

.EXAMPLE
    pwsh -NoProfile -File dev/capture-views.ps1
    pwsh -NoProfile -File dev/capture-views.ps1 -Set full -Label before-landing-makeover

.NOTES
    Sources of variance — two runs against an unchanged build are NOT byte-identical:

      1. Rendered timestamps. Attempt rows show CreatedAtUtc ("Pitch · 25 Jul 2026"),
         which the domain stamps when the fixture is seeded, not from the scenario's
         fixed StartUtc. A fixture reseeded on a different day renders different dates.
         (ProjectFingerprint does not cover these, so the fingerprint stays equal —
         equal fingerprints do not imply equal pixels.)
      2. Window geometry. The run maximises the window, so the corpus is the size of the
         primary display and two corpora captured on different machines differ
         everywhere. The observed size is recorded in capture.json; compare corpora only
         when it matches.
      3. Log panel contents. The bottom LOGS dock shows this session's Serilog lines
         including per-run timings. This is the dominant source: in a measured
         back-to-back pair (same build, same day, same window size), 8 of 10 views
         differed only in rows 814-878 — the LOGS dock — and 2 were byte-identical.
      4. Focus visuals. calibration-results additionally differed over 122 pixels at
         x 411-474, y 123-151: the focus ring on the inner Results tab, which lands
         differently depending on where the preceding `select` left keyboard focus.

    So: NOT byte-identical, and this corpus must not be described as a regression guard.
    It is a baseline to compare by eye. Cropping the LOGS dock out would get most of the
    way to determinism if that is ever wanted.
#>

[CmdletBinding()]
param(
    [ValidateSet('spine', 'full')]
    [string] $Set = 'spine',
    [string] $OutDir,
    [string] $Label,
    [switch] $KeepRunning,
    [switch] $NoMaximize,
    [switch] $Active,
    [switch] $NoElide,
    [switch] $InjectBrokenView
)

$ErrorActionPreference = 'Stop'

# geoDC is the nested clone at geodc/ (bootstrap.ps1); the UI driver runs from inside it, where its lease and
# fixtures resolve, and the corpus is written to the harness's own dev/captures/.
$harnessRoot = Split-Path $PSScriptRoot -Parent
$repoRoot = Join-Path $harnessRoot 'geodc'
$driver = Join-Path $repoRoot 'dev/ui.ps1'
$fixture = Join-Path $repoRoot 'dev/fixtures/probe.gdcproj'
$fixtureFingerprint = Join-Path $repoRoot 'dev/fixtures/probe.fingerprint.json'

# ---------------------------------------------------------------------------
# The view list. This is the single place a view is declared: adding, removing
# or re-routing a capture is an edit here and nowhere else.
#
# Each entry navigates from wherever the app happens to be, so views are
# independent of one another and of their order. Nav steps are `geodc-ui do` steps
# as argument arrays; the screenshot step is appended by the runner.
#
# Each view ends by *waiting on the thing whose appearance proves it arrived*,
# not by sleeping for a guessed duration. `wait --id X` blocks until X is in the
# tree and has been laid out (on screen, non-zero bounds), so it costs what the
# page actually takes. `click`/`select` need no lead-in wait either: the
# resolver already polls for the element they address.
#
# The chart surfaces carry their own readiness anchor: WebViewChartHost stamps
# RenderedAutomationId on the chart surface only once the JS shell reports a drawn
# frame, and retracts it again for each redraw. So `wait --id CoChartSurfacePanel`
# blocks on the presentation actually being on screen, and the guessed 900 ms
# settle this replaced is gone. Measured, the first arrival takes ~0.7-1.2s and
# the popup's chart ~4.6s — the old guess was not merely unprincipled, it was short.
#
# Fields on a view:
#   Full     — extended set only (-Set full).
#   Target   — 'app' (default) or 'sample' (the toolkit gallery). Views are
#              grouped by target so each is launched once; see the runner.
#   Window   — `--window` selector for the screenshot, for views that live in a
#              popup's own top-level window.
#   Volatile — navigation that is not idempotent (opening a popup). Excluded from
#              the elision optimisation in both directions; see below.
#   Cleanup  — steps run after the screenshot, to put the app back. Popups must
#              close, or every later view is captured underneath one.
#
# The spine is the eight shell pages plus the two Results tabs; the extended set
# adds sub-views that are individually addressable *and* show something their
# siblings don't. It is not meant to be exhaustive.
#
# Calibration and Verification select an attempt before capturing: with nothing
# selected both Results tabs render only "Select an attempt…", which would be a
# corpus that looks complete and shows nothing. The item names are fixture
# content — if the fixture changes, these fail loudly and want updating here.
# ---------------------------------------------------------------------------
$calibrationAttempt = 'Pitch calibration (INITIAL)'
$verificationAttempt = 'Pitch verification (INITIAL)'
$gnssAttempt = 'GNSS spot check (quayside)'

$views = @(
    @{
        Id = 'landing'
        Nav = @(@('click', '--id', 'LandingTab'), @('wait', '--id', 'LandingPage'))
    }
    @{
        Id = 'setup'
        Nav = @(@('click', '--id', 'SetupTab'), @('wait', '--id', 'SetupPage'))
    }
    @{
        # Survey opens on Features; the click makes that explicit rather than
        # relying on the page's default section.
        #
        # Every Survey section shares the 3-D viewport pane, a WebView2 chart that renders
        # after the list is ready. VrfChartSurfacePanel is the chart's own rendered anchor;
        # waiting on the list alone captured a blank viewport. This is the run's first
        # chart, so it also pays WebView2's cold start: hence the longer timeout.
        Id = 'survey'
        Nav = @(
            @('click', '--id', 'SurveyTab'),
            @('click', '--id', 'FeaturesTab'), @('wait', '--id', 'FeatureList'),
            @('wait', '--id', 'VrfChartSurfacePanel', '--timeout', '30')
        )
    }
    @{
        Id = 'survey-frame'
        Full = $true
        Nav = @(
            @('click', '--id', 'SurveyTab'),
            @('click', '--id', 'FrameTab'), @('wait', '--id', 'FrameOrientationPanel'),
            @('wait', '--id', 'VrfChartSurfacePanel', '--timeout', '30')
        )
    }
    @{
        Id = 'survey-sensors'
        Full = $true
        Nav = @(
            @('click', '--id', 'SurveyTab'),
            @('click', '--id', 'SensorsTab'), @('wait', '--id', 'SensorList'),
            @('wait', '--id', 'VrfChartSurfacePanel', '--timeout', '30')
        )
    }
    @{
        Id = 'survey-groups'
        Full = $true
        Nav = @(
            @('click', '--id', 'SurveyTab'),
            @('click', '--id', 'GroupsTab'), @('wait', '--id', 'FeatureGroupList'),
            @('wait', '--id', 'VrfChartSurfacePanel', '--timeout', '30')
        )
    }
    # No 'analysis' view: the shell hides its tab (Analysis is a placeholder), so there is
    # nothing to navigate to.
    @{
        Id = 'calibration-data-input'
        Nav = @(
            @('click', '--id', 'CalibrationTab'),
            @('select', '--id', 'AttemptList', '--item', $calibrationAttempt),
            @('click', '--id', 'DataInputTab'), @('wait', '--id', 'DataInputPanel')
        )
    }
    @{
        # The fixture's mobilisation cycle is left open (so the VRF can still be aligned), and an
        # open attempt has no result until Compute is pressed. The result then stays computed for
        # the rest of the launch.
        Id = 'calibration-results'
        Nav = @(
            @('click', '--id', 'CalibrationTab'),
            @('select', '--id', 'AttemptList', '--item', $calibrationAttempt),
            @('click', '--id', 'ResultsTab'), @('click', '--id', 'ComputeResultButton'),
            @('wait', '--id', 'CoSemanticsPanel'), @('wait', '--id', 'CoChartSurfacePanel')
        )
    }
    @{
        Id = 'verification-data-input'
        Nav = @(
            @('click', '--id', 'VerificationTab'),
            @('select', '--id', 'AttemptList', '--item', $verificationAttempt),
            @('click', '--id', 'DataInputTab'), @('wait', '--id', 'DataInputPanel')
        )
    }
    # No 'verification-results' view: a verification computes only against a completed calibration,
    # and the fixture's cycle is left open so the VRF can be aligned, so the view would be empty.
    @{
        # Shore Control shows the active job's copy; with none chosen it lists the operator's register,
        # so the capture shows whatever the capturing machine's register holds.
        Id = 'shore-control'
        Nav = @(@('click', '--id', 'ShoreControlTab'), @('wait', '--id', 'ShoreControlPage'))
    }
    @{
        # Selects the attempt for the same reason Calibration and Verification do: with none
        # selected both tabs render only their "add or select an attempt" prompt, which
        # would be a corpus that looks complete and shows nothing.
        Id = 'gnss'
        Nav = @(
            @('click', '--id', 'GnssTab'),
            @('select', '--id', 'AttemptList', '--item', $gnssAttempt),
            @('click', '--id', 'DataInputTab'), @('wait', '--id', 'DataInputPanel')
        )
    }
    @{
        Id = 'gnss-results'
        Full = $true
        Nav = @(
            @('click', '--id', 'GnssTab'),
            @('select', '--id', 'AttemptList', '--item', $gnssAttempt),
            # Results holds the plot and the data-point table; the assessment is in the inspector beside
            # it. The chart stays hidden until a result exists, and the fixture attempt is open with
            # none, so compute it first, as calibration-results does. The result then persists for
            # the rest of the launch.
            @('click', '--id', 'ResultsTab'),
            @('click', '--id', 'ComputeResultButton'),
            @('wait', '--id', 'GnssChartSurfacePanel', '--timeout', '30')
        )
    }
    @{
        # The one popup in the corpus. Immersive and non-modal, so it is its own top-level
        # window and the screenshot must name it — capturing 'main' here would render the
        # shell with a hole where the popup is. Unlike every other view this one is *not*
        # maximised: it is captured at the size the popup declares, which is the size a user
        # meets it at.
        #
        # Launched from the GNSS Data Input tab's Inspect button on the bound reference
        # dataset (the fixture attempt always has both datasets bound).
        #
        # Deliberately placed immediately after gnss-results, which shares its leading
        # GnssTab/attempt steps: that is the arrangement in which eliding would silently skip
        # the open, so keeping it here means every run exercises the Volatile guard.
        Id = 'data-analysis-popup'
        Full = $true
        Volatile = $true
        Window = 'title:GNSS Data Tool'
        Nav = @(
            @('click', '--id', 'GnssTab'),
            @('select', '--id', 'AttemptList', '--item', $gnssAttempt),
            @('click', '--id', 'DataInputTab'),
            @('click', '--id', 'InspectReferenceButton'),
            @('wait', '--id', 'AnalysisChartSurfacePanel')
        )
        Cleanup = @(@('click', '--id', 'CloseButton', '--scope', 'PopupPanel'))
    }
    @{
        # The settings surface: a nav rail indexing one scrolling document, and the only
        # popup that declares ScrollsBody = False, so it is also the only picture of a
        # frame whose body it does not scroll itself.
        #
        # Volatile for the same reason the analysis popup is: the open is a step the elision
        # pass would happily skip, and a skipped open captures the shell behind it instead.
        Id = 'settings-popup'
        Full = $true
        Volatile = $true
        Window = 'title:Settings'
        Nav = @(
            @('click', '--id', 'SettingsButton'),
            @('wait', '--id', 'SettingsSectionList')
        )
        Cleanup = @(@('click', '--id', 'CloseButton', '--scope', 'PopupPanel'))
    }
    @{
        # The same surface parked on its last section, which is the only view that shows
        # the tail padding: PadsTail extends the document so a short final section can
        # reach the top of the viewport, and the blank space below it is the cost. Nothing
        # else in the corpus pictures it, and it is the part of this design most likely to
        # look wrong rather than be wrong.
        Id = 'settings-popup-tail'
        Full = $true
        Volatile = $true
        Window = 'title:Settings'
        Nav = @(
            @('click', '--id', 'SettingsButton'),
            @('wait', '--id', 'SettingsSectionList'),
            @('click', '--id', 'ImportedMediaTab', '--scope', 'SettingsSectionList'),
            @('wait', '--id', 'SettingsSectionPanel')
        )
        Cleanup = @(@('click', '--id', 'CloseButton', '--scope', 'PopupPanel'))
    }

    # --- Toolkit gallery (Target = 'sample') -------------------------------------------
    # The proving ground the view-switcher decision calls "already proven in-tree", so it
    # needs a before picture of its own. One launch covers all of these because the runner
    # groups by target.
    #
    # Each is the *top* of its section: the gallery scrolls, and this script navigates with
    # click/select only, so anything below the fold is out of frame. `geodc-ui scroll` could
    # reach it (SampleScrollViewer supports ScrollPattern) — not done here because a
    # top-and-bottom pair per section doubles the gallery for a second-order gain.
    @{ Id = 'gallery-foundations'; Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'FoundationsNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-actions';     Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'ActionsNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-inputs';      Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'InputsNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-quantities';  Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'QuantitiesNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-conventions'; Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'ConventionsNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-collections'; Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'CollectionsNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-cards';       Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'CardsNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-sections';    Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'SectionsNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-progress';    Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'ProgressNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-navigation';  Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'NavigationNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-feedback';    Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'FeedbackNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-overlays';    Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'OverlaysNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
    @{ Id = 'gallery-chrome';      Full = $true; Target = 'sample'; Nav = @(@('click', '--id', 'ChromeNavItem'), @('wait', '--id', 'SelectedSampleSectionTitle')) }
)

if ($Set -eq 'spine')
{
    $views = @($views | Where-Object { -not $_.Full })
}

if ($InjectBrokenView)
{
    # One per code path a view can take, because they fail in different places: an app view
    # fails in the shared nav loop, a gallery view fails against the second target's session,
    # and the popup view can also fail at the screenshot (an unreachable --window), which is
    # past the point where nav has already reported success.
    $views += @{
        Id = 'deliberately-broken'
        Nav = @(@('click', '--id', 'NoSuchTabThatCannotExist'), @('settle', '--ms', '200'))
    }
    $views += @{
        Id = 'deliberately-broken-popup'
        Volatile = $true
        Window = 'title:NoSuchPopupWindowThatCannotExist'
        Nav = @(@('click', '--id', 'GnssTab'))
    }
    $views += @{
        Id = 'deliberately-broken-gallery'
        Target = 'sample'
        Nav = @(@('click', '--id', 'NoSuchNavItemThatCannotExist'), @('settle', '--ms', '200'))
    }
}

# ---------------------------------------------------------------------------

function Write-Progress-Line([string] $message)
{
    [Console]::Error.WriteLine($message)
}

# Runs one UI driver invocation through the shim and returns its parsed JSON lines.
# Every verb emits one JSON line per step; a non-ok line anywhere is a failure.
# `--target` is passed only for non-app targets: 'app' is the UI driver's own default, and omitting
# it keeps every existing invocation byte-identical to what it was. It goes immediately after
# the verb, never at the end — it is a flag on the verb, and `do` would fold a trailing one into
# its final step, silently running the whole batch against the default target instead.
function Invoke-UiDriver([string[]] $driverArgs, [string] $target = 'app')
{
    if ($target -ne 'app')
    {
        $driverArgs = @($driverArgs[0], '--target', $target) + @($driverArgs | Select-Object -Skip 1)
    }

    $lines = & pwsh -NoProfile -WorkingDirectory $repoRoot -File $driver @driverArgs 2>$null
    $parsed = @($lines | Where-Object { $_ -match '^\s*\{' } | ForEach-Object { $_ | ConvertFrom-Json })
    $failed = $parsed | Where-Object { -not $_.ok } | Select-Object -First 1
    return [pscustomobject]@{
        Steps   = $parsed
        Failure = $failed
        Raw     = $lines
    }
}

# Normalises a step list to an array *of* step arrays. PowerShell unrolls a single-element
# array of arrays down to the inner array, which would otherwise turn a one-step Cleanup into
# five one-token "steps".
function AsSteps($declared)
{
    $steps = @($declared)
    if ($steps.Count -gt 0 -and $steps[0] -isnot [System.Array]) { $steps = @(, $steps) }
    # `,` on the way out too: a bare `return` of a one-element array unrolls it again, which is
    # the very flattening this function exists to undo.
    return , $steps
}

# Joins nav steps into a single `geodc-ui do` argument list.
function Build-DoArgs([object[]] $steps)
{
    $flat = [System.Collections.Generic.List[string]]::new()
    $flat.Add('do')
    for ($i = 0; $i -lt $steps.Count; $i++)
    {
        if ($i -gt 0) { $flat.Add('++') }
        foreach ($token in $steps[$i]) { $flat.Add([string]$token) }
    }
    return $flat.ToArray()
}

function Fail([string] $code, [string] $message, $extra)
{
    $payload = @{ ok = $false; script = 'capture-views'; error = @{ code = $code; message = $message } }
    if ($extra) { $payload.error.details = $extra }
    [Console]::Out.WriteLine(($payload | ConvertTo-Json -Compress -Depth 6))
    exit 1
}

if (-not (Test-Path $fixture))
{
    Fail 'fixture-missing' "Fixture not found at $fixture. Run `dev/ui.ps1 fixture seed` first." $null
}

if (-not (Test-Path $fixtureFingerprint))
{
    Fail 'fixture-provenance-missing' `
        "No $fixtureFingerprint. A corpus without a fixture fingerprint cannot be compared honestly; re-run `dev/ui.ps1 fixture seed` to produce one." `
        $null
}

$fingerprint = Get-Content $fixtureFingerprint -Raw | ConvertFrom-Json

if (-not $Label)
{
    $commit = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
    $dirty = (& git -C $repoRoot status --porcelain 2>$null)
    $Label = if ($commit) { if ($dirty) { "$commit-dirty" } else { $commit } } else { 'unlabelled' }
}

if (-not $OutDir) { $OutDir = Join-Path $harnessRoot 'dev/captures' }
$corpusDir = Join-Path $OutDir $Label
New-Item -ItemType Directory -Force $corpusDir | Out-Null

Write-Progress-Line "Corpus: $corpusDir"
Write-Progress-Line "Fixture fingerprint: $($fingerprint.projectFingerprint)"

# Relaunch rather than reuse: a fresh process against the fixture is the only way a
# corpus means the same thing twice. Each target gets exactly one of these, which is why
# the runner below groups views by target instead of honouring declaration order.
function Start-Target([string] $target)
{
    Invoke-UiDriver @('kill') $target | Out-Null
    $attachArgs = if ($Active) { @('attach', '--launch') } else { @('attach', '--launch', '--passive') }
    $attach = Invoke-UiDriver $attachArgs $target
    if ($attach.Failure)
    {
        Fail 'app-launch-failed' "geodc-ui attach --launch failed for target '$target'." $attach.Failure.error
    }

    Write-Progress-Line "Target '$target': pid $($attach.Steps[0].data.pid) attached."

    # Maximise before capturing so scrollable regions show as much as the display allows.
    # Deliberately not `click --id MaxButton`: that button toggles, so a run starting from an
    # already-maximised window would restore it instead. Neither path needs `geodc-ui key`, so
    # the run stays unattended-safe.
    if (-not $NoMaximize)
    {
        if (-not ('Win32WindowPlacement' -as [type]))
        {
            Add-Type -Namespace '' -Name 'Win32WindowPlacement' -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr MonitorFromWindow(System.IntPtr hWnd, uint flags);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool GetMonitorInfo(System.IntPtr hMonitor, ref MonitorInfo info);

[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr after, int x, int y, int cx, int cy,
    uint flags);

// Fills the window's monitor work area without activating it or changing its z-order. ShowWindow has
// no maximise-without-activate command (SW_MAXIMIZE and SW_SHOWMAXIMIZED both activate), so a
// passive run sizes the restored window instead.
public static bool FillWorkArea(System.IntPtr hWnd)
{
    System.IntPtr monitor = MonitorFromWindow(hWnd, 2 /* MONITOR_DEFAULTTONEAREST */);
    MonitorInfo info = new MonitorInfo();
    info.Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MonitorInfo));
    if (monitor == System.IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) { return false; }

    const uint NoZOrder = 0x0004, NoActivate = 0x0010, NoOwnerZOrder = 0x0200;
    return SetWindowPos(hWnd, System.IntPtr.Zero, info.Work.Left, info.Work.Top,
        info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top, NoZOrder | NoActivate | NoOwnerZOrder);
}
'@
        }

        $handle = [System.IntPtr][System.Convert]::ToInt64([string]$attach.Steps[0].data.mainWindowHandle, 16)
        if ($Active)
        {
            # SW_MAXIMIZE: a true maximise, which also activates the window.
            [Win32WindowPlacement]::ShowWindow($handle, 3) | Out-Null
        }
        elseif (-not [Win32WindowPlacement]::FillWorkArea($handle))
        {
            Fail 'resize-failed' "Could not size target '$target' to its monitor work area." $null
        }

        # The one sleep with no element to wait on: every anchor is already laid out at the old
        # size, so `wait` would return instantly and prove nothing about the resize.
        Invoke-UiDriver @('settle', '--ms', '600') $target | Out-Null
        Write-Progress-Line "Target '$target': window $(if ($Active) { 'maximised' } else { 'sized to the work area' })."
    }
}

$captured = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[object]]::new()
$windowSizes = [ordered]@{}
$startedTargets = [System.Collections.Generic.List[string]]::new()
$elided = 0

# Group by target, preserving each target's own declaration order and the order the targets
# first appear. The alternative — honouring declaration order across targets — would relaunch
# an app every time the list crossed a boundary; grouping guarantees exactly one launch each.
$views = @($views | Group-Object { if ($_.Target) { [string]$_.Target } else { 'app' } } |
    ForEach-Object { $_.Group })

# Nav steps stay absolute in the declaration so views remain readable and order-independent,
# but re-walking them costs real time: every UI driver step is a round trip, and the sub-views
# re-click the shell tab they are already on. So the runner skips the leading steps it
# *just* executed successfully — clicking SurveyTab twice in a row, or reselecting the same
# attempt, is a no-op. Reset to nothing after any failure, since the app's position is then
# unknown; -NoElide turns it off wholesale when a capture looks like state has bled.
#
# Two things are never elided, because the optimisation's safety argument does not cover them.
# A target switch is not a change of position but a different process, so the boundary resets
# outright. And a Volatile view's navigation is not idempotent: skipping the click that opens a
# popup leaves nothing to capture, and repeating it could stack or toggle one. Volatile is
# excluded in both directions — nothing elides into it, and the view after it re-walks in full,
# since a Cleanup step has moved the app somewhere its own declaration never described.
$previousNav = @()
$currentTarget = $null

foreach ($view in $views)
{
    $viewId = [string]$view.Id
    $viewTarget = if ($view.Target) { [string]$view.Target } else { 'app' }
    $imagePath = Join-Path $corpusDir "$viewId.png"
    $nav = AsSteps $view.Nav

    if ($viewTarget -ne $currentTarget)
    {
        Start-Target $viewTarget
        $startedTargets.Add($viewTarget)
        $currentTarget = $viewTarget
        $previousNav = @()
    }

    $skip = 0
    if (-not $NoElide -and -not $view.Volatile)
    {
        while ($skip -lt $previousNav.Count -and $skip -lt $nav.Count -and
            (($previousNav[$skip] -join "`u{1}") -eq ($nav[$skip] -join "`u{1}")))
        {
            $skip++
        }

        # Never skip the whole navigation: a view must always assert where it is.
        if ($skip -ge $nav.Count) { $skip = $nav.Count - 1 }
    }

    $elided += $skip
    $shotStep = @('screenshot', '--out', $imagePath)
    if ($view.Window) { $shotStep += @('--window', [string]$view.Window) }
    $steps = @($nav | Select-Object -Skip $skip) + @(, $shotStep)

    Write-Progress-Line ("  capturing $viewId ..." + $(if ($skip) { " (skipped $skip repeated step(s))" } else { '' }))
    $result = Invoke-UiDriver (Build-DoArgs $steps) $viewTarget

    # Cleanup runs whether or not the capture succeeded: a popup left open would be in every
    # later image. Its own failure is reported, but never masks the capture failure above it.
    $cleanupFailure = $null
    if ($view.Cleanup)
    {
        $cleanup = Invoke-UiDriver (Build-DoArgs (AsSteps $view.Cleanup)) $viewTarget
        $cleanupFailure = $cleanup.Failure
    }

    if ($result.Failure -or $cleanupFailure)
    {
        $previousNav = @()
        $failed = if ($result.Failure) { $result.Failure } else { $cleanupFailure }
        $phase = if ($result.Failure) { 'capture' } else { 'cleanup' }
        $failures.Add([pscustomobject]@{
                view  = $viewId
                target = $viewTarget
                phase = $phase
                step  = $failed.step
                verb  = $failed.verb
                code  = $failed.error.code
                error = $failed.error.message
            })
        Write-Progress-Line "  FAILED  $viewId ($phase) — $($failed.verb) step $($failed.step): $($failed.error.code)"
        continue
    }

    $previousNav = if ($view.Volatile) { @() } else { $nav }

    if (-not (Test-Path $imagePath) -or (Get-Item $imagePath).Length -eq 0)
    {
        $failures.Add([pscustomobject]@{
                view  = $viewId
                target = $viewTarget
                code  = 'image-missing'
                error = "the UI driver reported success but no image was written to $imagePath."
            })
        Write-Progress-Line "  FAILED  $viewId — no image written"
        continue
    }

    $shot = $result.Steps | Where-Object { $_.verb -eq 'screenshot' } | Select-Object -Last 1

    # Per target, not per run: the gallery and the app are separate windows and need not be
    # the same size, so one global number would misdescribe one of them.
    if (-not $view.Window) { $windowSizes[$viewTarget] = "$($shot.data.width)x$($shot.data.height)" }

    # Recorded because it is the evidence that the readiness anchors are honest: a waitedMs
    # near one UIA search means the page was ready on first look and any sleep here would
    # have been dead time. A large or growing value is the signal to look at that view.
    $waits = @($result.Steps | Where-Object { $_.verb -eq 'wait' } |
        ForEach-Object { [pscustomobject]@{ id = $_.data.elementId; waitedMs = $_.data.waitedMs; readyOnFirstLook = $_.data.readyOnFirstLook } })

    $captured.Add([pscustomobject]@{
            view   = $viewId
            target = $viewTarget
            window = if ($view.Window) { [string]$view.Window } else { 'main' }
            file   = "$viewId.png"
            width  = $shot.data.width
            height = $shot.data.height
            waits  = $waits
        })
}

$provenance = [ordered]@{
    label              = $Label
    set                = $Set
    maximised          = (-not $NoMaximize)
    mode               = $(if ($Active) { 'active' } else { 'passive' })
    capturedUtc        = [DateTime]::UtcNow.ToString('o')
    appCommit          = (& git -C $repoRoot rev-parse HEAD 2>$null)
    workingTreeDirty   = [bool](& git -C $repoRoot status --porcelain 2>$null)
    targets            = @($startedTargets)
    fixture            = [ordered]@{
        # Scoped deliberately: the sample gallery opens no project, so this provenance describes
        # only the views whose target is 'app'. Writing it at the top level would claim a fixture
        # for the gallery views, which do not have one.
        appliesToTarget    = 'app'
        path               = $fixture
        seed               = $fingerprint.seed
        scenario           = $fingerprint.scenario
        projectFingerprint = $fingerprint.projectFingerprint
        domainFingerprint  = $fingerprint.domainFingerprint
        registerFingerprint = $fingerprint.registerFingerprint
        generatedUtc       = $fingerprint.generatedUtc
    }
    windowSizes        = $windowSizes
    elidedSteps        = $elided
    views              = $captured
    failures           = $failures
    varianceKnownSources = @(
        'rendered CreatedAtUtc timestamps in attempt rows (fixture seed date)',
        'window geometry restored from app settings',
        'LOGS dock contents (per-session Serilog lines)',
        'focus ring placement after select (seen on calibration-results)',
        'chart rendering: the WebView2 surfaces redraw per presentation and are not pixel-stable'
    )
}

$provenance | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $corpusDir 'capture.json') -Encoding utf8

# Every target this run started, not just the last: a two-target run otherwise leaves the
# first one alive, holding its lease and its fixture workspace.
if (-not $KeepRunning)
{
    foreach ($started in ($startedTargets | Select-Object -Unique))
    {
        Invoke-UiDriver @('kill') $started | Out-Null
    }
}

$summary = [ordered]@{
    ok       = ($failures.Count -eq 0)
    script   = 'capture-views'
    label    = $Label
    set      = $Set
    corpus   = $corpusDir
    captured = $captured.Count
    failures = $failures
}

[Console]::Out.WriteLine(($summary | ConvertTo-Json -Compress -Depth 6))

if ($failures.Count -gt 0)
{
    Write-Progress-Line "Unreachable views: $(($failures | ForEach-Object { $_.view }) -join ', ')"
    exit 1
}

Write-Progress-Line "Captured $($captured.Count) view(s) to $corpusDir"
exit 0
